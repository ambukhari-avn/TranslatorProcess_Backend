using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TranslationBackend.API.Hubs;
using TranslationBackend.API.Middleware;
using TranslationBackend.Application;
using TranslationBackend.Application.Interface.IRepo;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Application.Services;
using TranslationBackend.Domain.Model;
using TranslationBackend.Infrastructure.Auth;
using TranslationBackend.Infrastructure.Background;
using TranslationBackend.Infrastructure.Data;
using TranslationBackend.Infrastructure.External;
using TranslationBackend.Infrastructure.Repositories;
using TranslationBackend.Infrastructure.Storage;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------- options
builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
builder.Services.Configure<MicrosoftOptions>(config.GetSection(MicrosoftOptions.Section));
builder.Services.Configure<AuthOptions>(config.GetSection(AuthOptions.Section));
builder.Services.Configure<ExtractionOptions>(config.GetSection(ExtractionOptions.Section));
builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
builder.Services.Configure<JobOptions>(config.GetSection(JobOptions.Section));

var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key is not configured (at least 32 characters). Set it with user-secrets or the Jwt__Key environment variable.");
var jobOptions = config.GetSection(JobOptions.Section).Get<JobOptions>() ?? new JobOptions();

// ---------------------------------------------------------------- database and identity
if (!builder.Environment.IsEnvironment("Testing"))      // the integration tests register an in-memory database themselves
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(config.GetConnectionString("DefaultConnection")));

// Sign-in is Microsoft only, so there are no password rules.
builder.Services
    .AddIdentity<Users, IdentityRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 0;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// ---------------------------------------------------------------- JWT
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    };

    // Browsers cannot set an Authorization header on a WebSocket, so SignalR sends ?access_token=.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                context.Token = accessToken;
            return Task.CompletedTask;
        },
    };
});
builder.Services.AddAuthorization();

// ---------------------------------------------------------------- application services
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();

builder.Services.AddHttpClient<IMicrosoftAuthClient, MicrosoftAuthClient>();
builder.Services.AddHttpClient<IExtractionClient, ExtractionClient>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

builder.Services.AddScoped<IEmployeeRepo, EmployeeRepo>();
builder.Services.AddScoped<IJobRepo, JobRepo>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IJobService, JobService>();

if (config.GetValue("Background:Enabled", true))
{
    builder.Services.AddHostedService<JobPollingService>();
    builder.Services.AddHostedService<FileCleanupService>();
}

// ---------------------------------------------------------------- MVC, uploads, CORS, Swagger
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(o =>
    {
        // Same { code, message } shape as every other error of the translation endpoints.
        o.InvalidModelStateResponseFactory = context =>
        {
            var message = context.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m))
                          ?? "The request is not valid.";
            return new BadRequestObjectResult(new { code = "VALIDATION_ERROR", message });
        };
    });

var bodyLimit = jobOptions.MaxUploadBytes + 1024 * 1024;       // the file plus the form fields
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = bodyLimit);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = bodyLimit);

var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(allowedOrigins)
          .AllowAnyMethod()
          .AllowAnyHeader()
          .AllowCredentials()                                   // SignalR's handshake needs credentials, so no wildcard origin
          .WithExposedHeaders("Content-Disposition")));         // lets the browser read the download's file name

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Translation Backend API", Version = "v1", Description = "Document translation: sign-in, uploads, progress, downloads, history." });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "The token returned by the Microsoft callback",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() },
    });
    c.UseInlineDefinitionsForEnums();
});

var app = builder.Build();

// ---------------------------------------------------------------- start-up work: migrate, seed
if (!EF.IsDesignTime)       // the EF tooling loads this app to read the model; it must not touch a database
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    if (config.GetValue("Database:MigrateOnStartup", false))
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

    var seeds = config.GetSection("Seed:Users").Get<List<SeedUser>>() ?? [];
    try
    {
        await DbSeeder.SeedAsync(scope.ServiceProvider, seeds, logger);
    }
    catch (Exception ex)
    {
        // A missing table (migrations not applied yet) must not stop the app from starting and showing why.
        logger.LogError(ex, "Seeding the privileged accounts failed. Have the migrations been applied?");
    }
}

// ---------------------------------------------------------------- pipeline
app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Translation Backend API v1");
    c.RoutePrefix = "swagger";
});

var hasFrontend = File.Exists(Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html"));
if (hasFrontend)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseMiddleware<ActiveUserMiddleware>();      // re-checks the user on every request: a deactivated user is cut off at once
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
if (hasFrontend) app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Visible to the integration tests (WebApplicationFactory&lt;Program&gt;).</summary>
public partial class Program { }
