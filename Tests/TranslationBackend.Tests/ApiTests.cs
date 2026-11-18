using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Infrastructure.Data;

namespace TranslationBackend.Tests;

/// <summary>The whole API in memory: real controllers, filters, middleware, JWT and services; a fake extraction service.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public FakeExtractionClient Extraction { get; } = new();
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "tb-api-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbName = Guid.NewGuid().ToString();

    public ApiFactory()
    {
        // Program reads its configuration before the test can intervene, so the test settings travel as environment variables.
        Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-signing-key-0123456789-abcdef");
        Environment.SetEnvironmentVariable("Storage__Root", Root);
        Environment.SetEnvironmentVariable("Background__Enabled", "false");
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("Seed__Users__0__Email", "");
        Environment.SetEnvironmentVariable("Microsoft__ClientId", "test-client");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
            services.RemoveAll<IExtractionClient>();
            services.AddSingleton<IExtractionClient>(Extraction);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}

public class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApiTests(ApiFactory factory) => _factory = factory;

    /// <summary>Creates a user and returns a client that is signed in as them.</summary>
    private async Task<(Users User, HttpClient Client)> SignedIn(Role role = Role.Employee, bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Users>>();
        var name = $"user{Guid.NewGuid():N}"[..14];
        var user = new Users
        {
            Name = name, UserName = name, Email = $"{name}@avanceon.ae", Role = role, IsActive = active,
        };
        Assert.True((await users.CreateAsync(user)).Succeeded);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateToken(user));
        return (user, client);
    }

    private static MultipartFormDataContent Upload(string fileName, byte[] bytes, string? language = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "File", fileName);
        if (language != null) form.Add(new StringContent(language), "Language");
        return form;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>());

    private async Task<Guid> StartJob(HttpClient client, string name = "report.pdf")
    {
        var response = await client.PostAsync("/api/documents", Upload(name, TestData.Pdf()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Guid.Parse((await Json(response)).GetProperty("id").GetString()!);
    }

    // ------------------------------------------------------------------ authentication
    [Fact]
    public async Task The_translation_endpoints_need_a_token()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/employees")).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_user_is_cut_off_even_with_a_valid_token()
    {
        var (_, off) = await SignedIn(active: false);
        var response = await off.GetAsync("/api/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await Json(response);
        Assert.True(body.GetProperty("deactivated").GetBoolean());
    }

    [Fact]
    public async Task The_sign_in_endpoints_are_public_and_keep_the_supply_chain_shapes()
    {
        var anonymous = _factory.CreateClient();

        var login = await Json(await anonymous.GetAsync("/api/auth/microsoft/login"));
        var url = login.GetProperty("authUrl").GetString()!;
        Assert.Contains("client_id=test-client", url);
        Assert.Contains("state=LOGIN", url);

        var register = await Json(await anonymous.GetAsync("/api/auth/microsoft/register"));
        Assert.Contains("state=REGISTER", register.GetProperty("authUrl").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/auth/microsoft/callback")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/auth/developers")).StatusCode);
    }

    // ------------------------------------------------------------------ translations
    [Fact]
    public async Task An_employee_uploads_a_document_and_follows_it()
    {
        var (_, client) = await SignedIn();
        var before = _factory.Extraction.Started.Count;

        var response = await client.PostAsync("/api/documents", Upload("Maintenance.pdf", TestData.Pdf(), "ru"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var job = await Json(response);
        Assert.Equal("Queued", job.GetProperty("status").GetString());
        Assert.Equal("Maintenance.pdf", job.GetProperty("originalName").GetString());
        Assert.Equal(before + 1, _factory.Extraction.Started.Count);

        var id = job.GetProperty("id").GetString();
        var read = await Json(await client.GetAsync($"/api/documents/{id}"));
        Assert.Equal(id, read.GetProperty("id").GetString());

        var list = await Json(await client.GetAsync("/api/documents"));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Bad_uploads_get_a_code_and_a_message()
    {
        var (_, client) = await SignedIn();

        var text = await client.PostAsync("/api/documents", Upload("notes.txt", new byte[] { 1, 2, 3 }));
        Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
        Assert.Equal("UNSUPPORTED_FILE_TYPE", (await Json(text)).GetProperty("code").GetString());

        var fake = await client.PostAsync("/api/documents", Upload("fake.pdf", System.Text.Encoding.ASCII.GetBytes("not a pdf")));
        Assert.Equal("INVALID_FILE", (await Json(fake)).GetProperty("code").GetString());

        var noFile = await client.PostAsync("/api/documents", new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(noFile)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task One_user_cannot_see_another_users_job_but_a_developer_can()
    {
        var (_, owner) = await SignedIn();
        var (_, other) = await SignedIn();
        var (_, developer) = await SignedIn(Role.SuperUser);
        var id = await StartJob(owner);

        var hidden = await other.GetAsync($"/api/documents/{id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal("JOB_NOT_FOUND", (await Json(hidden)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/documents/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/documents/{id}/download")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync($"/api/documents/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_finished_translation_can_be_downloaded_with_its_file_name()
    {
        var (_, client) = await SignedIn();
        var id = await StartJob(client, "Pump manual.pdf");

        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/documents/{id}/download")).StatusCode);

        var output = Path.Combine(_factory.Root, id.ToString("D"), "output", "x_ru_replica.docx");
        var review = Path.Combine(_factory.Root, id.ToString("D"), "output", "x_ru_review.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, "translated bytes");
        await File.WriteAllTextAsync(review, "review bytes");
        _factory.Extraction.Finish(id.ToString("D"), output, review);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobService>().RefreshAsync(id);

        var status = await Json(await client.GetAsync($"/api/documents/{id}"));
        Assert.Equal("Done", status.GetProperty("status").GetString());
        Assert.True(status.GetProperty("canDownload").GetBoolean());

        var download = await client.GetAsync($"/api/documents/{id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("translated bytes", await download.Content.ReadAsStringAsync());
        Assert.Contains("Pump manual_RU.docx", download.Content.Headers.ContentDisposition!.ToString().Replace("\"", ""));

        var reviewDownload = await client.GetAsync($"/api/documents/{id}/review");
        Assert.Equal("review bytes", await reviewDownload.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_job_can_be_cancelled_once()
    {
        var (_, client) = await SignedIn();
        var id = await StartJob(client);

        var cancelled = await client.PostAsync($"/api/documents/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await Json(cancelled)).GetProperty("status").GetString());

        var again = await client.PostAsync($"/api/documents/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("JOB_FINISHED", (await Json(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_history_is_for_developers_only_and_shows_who_ran_what()
    {
        var (user, employee) = await SignedIn();
        var (_, developer) = await SignedIn(Role.SuperUser);
        await StartJob(employee, "history-check.pdf");

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/history")).StatusCode);

        var history = await Json(await developer.GetAsync($"/api/history?userId={user.Id}"));
        var item = history.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("history-check.pdf", item.GetProperty("originalName").GetString());
        Assert.Equal(user.Email, item.GetProperty("userEmail").GetString());
    }

    // ------------------------------------------------------------------ employees
    [Fact]
    public async Task A_developer_can_deactivate_and_reactivate_an_employee_and_an_employee_cannot()
    {
        var (_, developer) = await SignedIn(Role.SuperUser);
        var (_, employee) = await SignedIn();
        var (target, _) = await SignedIn();

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PatchAsJsonAsync($"/api/employees/{target.Id}/status", new { isActive = false })).StatusCode);

        var off = await developer.PatchAsJsonAsync($"/api/employees/{target.Id}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await Json(off)).GetProperty("isActive").GetBoolean());

        var on = await developer.PatchAsJsonAsync($"/api/employees/{target.Id}/status", new { isActive = true });
        Assert.True((await Json(on)).GetProperty("isActive").GetBoolean());

        var logs = await Json(await developer.GetAsync("/api/audit-logs"));
        Assert.Contains(logs.GetProperty("items").EnumerateArray(), l => l.GetProperty("action").GetString()!.StartsWith("Deactivated employee"));
    }

    [Fact]
    public async Task A_developer_cannot_deactivate_themselves()
    {
        var (admin, client) = await SignedIn(Role.SuperUser);

        var off = await client.PatchAsJsonAsync($"/api/employees/{admin.Id}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.BadRequest, off.StatusCode);
    }

    // ------------------------------------------------------------------ operations
    [Fact]
    public async Task Health_reports_the_database_and_the_translation_service_without_a_token()
    {
        var anonymous = _factory.CreateClient();

        var ok = await Json(await anonymous.GetAsync("/api/health"));
        Assert.Equal("ok", ok.GetProperty("status").GetString());

        _factory.Extraction.Unavailable = true;
        try
        {
            var degraded = await anonymous.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, degraded.StatusCode);
            var body = await Json(degraded);
            Assert.Equal("degraded", body.GetProperty("status").GetString());
            Assert.False(body.GetProperty("extraction").GetBoolean());
        }
        finally
        {
            _factory.Extraction.Unavailable = false;
        }
    }

    [Fact]
    public async Task Swagger_is_generated_for_every_endpoint()
    {
        var response = await _factory.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = (await response.Content.ReadAsStringAsync()).ToLowerInvariant();    // controller routes keep their class-name casing
        Assert.Contains("/api/documents", text);
        Assert.Contains("/api/history", text);
        Assert.Contains("/api/auth/microsoft/callback", text);
    }
}
