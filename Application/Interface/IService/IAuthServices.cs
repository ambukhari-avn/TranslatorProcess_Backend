using TranslationBackend.Application.DTOs;
using TranslationBackend.Domain.Model;

namespace TranslationBackend.Application.Interface.IService
{
    /// <summary>Signs a user in after Microsoft has authenticated them, applying the approval workflow.</summary>
    public interface IAuthService
    {
        string BuildAuthUrl(string state);

        IReadOnlyList<DeveloperContactDto> GetDevelopers();

        /// <summary>Exchange the Microsoft authorization code, find or create the user, and decide what they may do.</summary>
        Task<MicrosoftSignInResult> SignInWithMicrosoftAsync(string code, CancellationToken ct = default);
    }

    public enum SignInOutcome
    {
        /// <summary>A token was issued.</summary>
        Success,
        Deactivated,
        /// <summary>Microsoft did not give a usable answer (bad code, no email, network).</summary>
        Failed,
    }

    public record MicrosoftSignInResult(
        SignInOutcome Outcome,
        string? Token = null,
        Users? User = null,
        string? Message = null,
        bool NewlyRegistered = false);

    public interface IJwtTokenService
    {
        string CreateToken(Users user);
    }

    /// <summary>The three calls of the Microsoft OAuth flow.</summary>
    public interface IMicrosoftAuthClient
    {
        string BuildAuthUrl(string state);
        Task<MicrosoftTokenResponseDto?> ExchangeCodeAsync(string code, CancellationToken ct = default);
        Task<MicrosoftUserInfoDto?> GetUserInfoAsync(string accessToken, CancellationToken ct = default);
    }
}
