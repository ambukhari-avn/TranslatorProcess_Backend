using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Utils;

namespace TranslationBackend.Application.Services
{
    /// <summary>
    /// The sign-in rules: Microsoft proves who the person is, and the only check is the allowed email domain
    /// (avanceon.ae). The first sign-in creates an active Employee; a deactivated account gets no token.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly UserManager<Users> _userManager;
        private readonly IMicrosoftAuthClient _microsoft;
        private readonly IJwtTokenService _jwt;
        private readonly IAuditService _audit;
        private readonly IRealtimeNotifier _notifier;
        private readonly AuthOptions _options;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            UserManager<Users> userManager,
            IMicrosoftAuthClient microsoft,
            IJwtTokenService jwt,
            IAuditService audit,
            IRealtimeNotifier notifier,
            IOptions<AuthOptions> options,
            ILogger<AuthService> logger)
        {
            _userManager = userManager;
            _microsoft = microsoft;
            _jwt = jwt;
            _audit = audit;
            _notifier = notifier;
            _options = options.Value;
            _logger = logger;
        }

        public string BuildAuthUrl(string state) => _microsoft.BuildAuthUrl(state);

        /// <summary>Active, approved Developers (SuperUsers): who a person waiting for approval should write to.</summary>
        public IReadOnlyList<DeveloperContactDto> GetDevelopers()
            => _userManager.Users
                .Where(u => u.Role == Role.SuperUser && u.IsActive)
                .OrderBy(u => u.Name)
                .Select(u => new DeveloperContactDto(u.Name, u.Email))
                .ToList();

        public async Task<MicrosoftSignInResult> SignInWithMicrosoftAsync(string code, CancellationToken ct = default)
        {
            var token = await _microsoft.ExchangeCodeAsync(code, ct);
            if (token == null)
                return new MicrosoftSignInResult(SignInOutcome.Failed, Message: "Failed to obtain an access token from Microsoft. Check the backend log for details.");

            var info = await _microsoft.GetUserInfoAsync(token.AccessToken, ct);
            if (info == null)
                return new MicrosoftSignInResult(SignInOutcome.Failed, Message: "Failed to retrieve your profile from Microsoft. Check the backend log for details.");

            var email = info.Mail ?? info.UserPrincipalName;
            if (string.IsNullOrWhiteSpace(email))
                return new MicrosoftSignInResult(SignInOutcome.Failed, Message: "Your Microsoft account did not provide an email address.");

            if (!DomainAllowed(email))
            {
                _logger.LogWarning("Sign-in refused for {Email}: domain not allowed", email);
                return new MicrosoftSignInResult(SignInOutcome.Failed,
                    Message: $"Only {string.Join(", ", _options.AllowedEmailDomains.Select(d => "@" + d))} accounts can use this application.");
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return await RegisterAsync(info, email);

            if (!user.IsActive)
                return new MicrosoftSignInResult(SignInOutcome.Deactivated, User: user,
                    Message: "You don't have access. Your account has been deactivated; contact a Developer to be reactivated.");

            await _audit.LogAsync($"Signed in via Microsoft ({user.Role})", user.Id, user.UserName, "Auth", user.Id, ct);
            return new MicrosoftSignInResult(SignInOutcome.Success, _jwt.CreateToken(user), user);
        }

        /// <summary>First sign-in: an active Employee is created and signed in at once.</summary>
        private async Task<MicrosoftSignInResult> RegisterAsync(MicrosoftUserInfoDto info, string email)
        {
            var username = $"{email.Split('@')[0]}_ms";
            if (await _userManager.FindByNameAsync(username) != null)
                username = $"{username}_{Guid.NewGuid().ToString()[..4]}";

            var user = new Users
            {
                Name = string.IsNullOrWhiteSpace(info.DisplayName) ? email : info.DisplayName,
                UserName = username,
                Email = email,
                // Public sign-in always creates a regular Employee; SuperUsers come from the seed list.
                Role = Role.Employee,
                IsActive = true,
                EmailConfirmed = true,
                CreatedAt = DateTimeHelper.Now,
                UpdatedAt = DateTimeHelper.Now,
            };
            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
                return new MicrosoftSignInResult(SignInOutcome.Failed, Message: string.Join("; ", result.Errors.Select(e => e.Description)));

            await _audit.LogAsync($"New employee {user.Name} ({user.Email}) signed in for the first time", user.Id, user.UserName, "Auth", user.Id);
            await _notifier.EmployeeChangedAsync("registered", user.Id);

            return new MicrosoftSignInResult(SignInOutcome.Success, _jwt.CreateToken(user), user, NewlyRegistered: true);
        }

        private bool DomainAllowed(string email)
        {
            if (_options.AllowedEmailDomains.Length == 0) return true;
            var at = email.LastIndexOf('@');
            if (at < 0) return false;
            var domain = email[(at + 1)..];
            return _options.AllowedEmailDomains.Any(d => string.Equals(d.TrimStart('@'), domain, StringComparison.OrdinalIgnoreCase));
        }
    }
}
