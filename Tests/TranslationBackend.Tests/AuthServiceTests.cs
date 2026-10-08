using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Application.Services;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Infrastructure.Auth;
using TranslationBackend.Infrastructure.Data;

namespace TranslationBackend.Tests;

public class FakeMicrosoft : IMicrosoftAuthClient
{
    public MicrosoftTokenResponseDto? Token { get; set; } = new() { AccessToken = "ms-token" };
    public MicrosoftUserInfoDto? Profile { get; set; }

    public string BuildAuthUrl(string state) => $"https://login.example/authorize?state={state}";
    public Task<MicrosoftTokenResponseDto?> ExchangeCodeAsync(string code, CancellationToken ct = default) => Task.FromResult(Token);
    public Task<MicrosoftUserInfoDto?> GetUserInfoAsync(string accessToken, CancellationToken ct = default) => Task.FromResult(Profile);
}

public class AuthServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly UserManager<Users> _users;
    private readonly FakeMicrosoft _microsoft = new();
    private readonly FakeAudit _audit = new();
    private readonly FakeNotifier _notifier = new();
    private AuthService _service;

    public AuthServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<Users>(o =>
        {
            o.User.RequireUniqueEmail = true;
            o.Password.RequiredLength = 0;
        }).AddEntityFrameworkStores<AppDbContext>();
        _provider = services.BuildServiceProvider();
        _users = _provider.GetRequiredService<UserManager<Users>>();
        _service = Create(new AuthOptions());
    }

    public void Dispose() => _provider.Dispose();

    private AuthService Create(AuthOptions options) => new(
        _users, _microsoft,
        new JwtTokenService(Options.Create(new JwtOptions { Key = "0123456789012345678901234567890123456789" })),
        _audit, _notifier, Options.Create(options), NullLogger<AuthService>.Instance);

    private void Signs(string email, string name = "Sara Khan")
        => _microsoft.Profile = new MicrosoftUserInfoDto { DisplayName = name, Mail = email, UserPrincipalName = email };

    private async Task<Users> Existing(string email, bool active, Role role = Role.Employee)
    {
        var user = new Users
        {
            Name = "Existing", UserName = email.Split('@')[0] + "_ms", Email = email,
            Role = role, IsActive = active,
        };
        Assert.True((await _users.CreateAsync(user)).Succeeded);
        return user;
    }

    [Fact]
    public async Task A_first_sign_in_creates_an_active_employee_and_returns_a_token()
    {
        Signs("sara@avanceon.ae");

        var result = await _service.SignInWithMicrosoftAsync("code");

        Assert.Equal(SignInOutcome.Success, result.Outcome);
        Assert.True(result.NewlyRegistered);
        Assert.False(string.IsNullOrEmpty(result.Token));
        var user = await _users.FindByEmailAsync("sara@avanceon.ae");
        Assert.NotNull(user);
        Assert.Equal((Role.Employee, true), (user!.Role, user.IsActive));
        Assert.Equal("sara_ms", user.UserName);
        Assert.Contains(_audit.Actions, a => a.Contains("signed in for the first time"));
    }

    [Fact]
    public async Task A_deactivated_user_gets_no_token()
    {
        await Existing("off@avanceon.ae", false);

        Signs("off@avanceon.ae");
        var off = await _service.SignInWithMicrosoftAsync("c");
        Assert.Equal(SignInOutcome.Deactivated, off.Outcome);
        Assert.Null(off.Token);
    }

    [Fact]
    public async Task An_active_user_gets_a_token_and_the_sign_in_is_audited()
    {
        await Existing("ok@avanceon.ae", true, Role.SuperUser);
        Signs("ok@avanceon.ae");

        var result = await _service.SignInWithMicrosoftAsync("c");

        Assert.Equal(SignInOutcome.Success, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.Equal(Role.SuperUser, result.User!.Role);
        Assert.Contains(_audit.Actions, a => a.StartsWith("Signed in via Microsoft"));
    }

    [Fact]
    public async Task Microsoft_failures_are_reported_without_creating_anyone()
    {
        _microsoft.Token = null;
        Assert.Equal(SignInOutcome.Failed, (await _service.SignInWithMicrosoftAsync("bad")).Outcome);

        _microsoft.Token = new() { AccessToken = "t" };
        _microsoft.Profile = null;
        Assert.Equal(SignInOutcome.Failed, (await _service.SignInWithMicrosoftAsync("c")).Outcome);

        _microsoft.Profile = new MicrosoftUserInfoDto { DisplayName = "No Mail" };
        Assert.Equal(SignInOutcome.Failed, (await _service.SignInWithMicrosoftAsync("c")).Outcome);
        Assert.Empty(_users.Users);
    }

    [Fact]
    public async Task Only_the_allowed_email_domains_can_sign_in()
    {
        _service = Create(new AuthOptions { AllowedEmailDomains = ["avanceon.ae"] });

        Signs("stranger@gmail.com");
        var refused = await _service.SignInWithMicrosoftAsync("c");
        Assert.Equal(SignInOutcome.Failed, refused.Outcome);
        Assert.Contains("@avanceon.ae", refused.Message);
        Assert.Null(await _users.FindByEmailAsync("stranger@gmail.com"));

        Signs("Colleague@AVANCEON.AE");
        Assert.Equal(SignInOutcome.Success, (await _service.SignInWithMicrosoftAsync("c")).Outcome);
    }

    [Fact]
    public async Task The_contact_list_shows_only_active_developers()
    {
        await Existing("dev@avanceon.ae", true, Role.SuperUser);
        await Existing("gone@avanceon.ae", false, Role.SuperUser);
        await Existing("emp@avanceon.ae", true);

        var developers = _service.GetDevelopers();

        Assert.Equal(["dev@avanceon.ae"], developers.Select(o => o.Email));
    }

    [Fact]
    public async Task Seeded_accounts_are_created_approved_and_existing_ones_are_promoted()
    {
        await Existing("promote@avanceon.ae", false);

        await DbSeeder.SeedAsync(_provider, [
            new SeedUser { Name = "New Dev", Email = "newdev@avanceon.ae", Role = Role.SuperUser },
            new SeedUser { Name = "Promoted", Email = "promote@avanceon.ae", Role = Role.SuperUser },
            new SeedUser { Name = "Blank", Email = "", Role = Role.SuperUser },
        ], NullLogger.Instance);
        await DbSeeder.SeedAsync(_provider, [new SeedUser { Name = "New Dev", Email = "newdev@avanceon.ae", Role = Role.SuperUser }], NullLogger.Instance);

        var created = await _users.FindByEmailAsync("newdev@avanceon.ae");
        Assert.Equal((Role.SuperUser, true), (created!.Role, created.IsActive));
        var promoted = await _users.FindByEmailAsync("promote@avanceon.ae");
        Assert.Equal((Role.SuperUser, true), (promoted!.Role, promoted.IsActive));
        Assert.Equal(2, _users.Users.Count());              // the blank entry was skipped, the repeat created no duplicate
    }
}
