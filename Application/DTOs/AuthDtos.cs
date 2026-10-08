using System.Text.Json.Serialization;

namespace TranslationBackend.Application.DTOs
{
    public class MicrosoftAuthUrlDto
    {
        public string AuthUrl { get; set; } = string.Empty;
    }

    public class MicrosoftTokenResponseDto
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("token_type")] public string TokenType { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
    }

    public class MicrosoftUserInfoDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("givenName")] public string GivenName { get; set; } = string.Empty;
        [JsonPropertyName("surname")] public string Surname { get; set; } = string.Empty;
        [JsonPropertyName("userPrincipalName")] public string UserPrincipalName { get; set; } = string.Empty;
        [JsonPropertyName("mail")] public string? Mail { get; set; }
    }

    /// <summary>One entry of the public list of Developer contacts shown on the "awaiting approval" screen.</summary>
    public record DeveloperContactDto(string Name, string? Email);
}
