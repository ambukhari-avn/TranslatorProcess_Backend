using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;

namespace TranslationBackend.Infrastructure.Auth;

/// <summary>The three calls of the Microsoft OAuth flow: the sign-in URL, the code exchange and the Graph profile.</summary>
public class MicrosoftAuthClient : IMicrosoftAuthClient
{
    private readonly HttpClient _http;
    private readonly MicrosoftOptions _options;
    private readonly ILogger<MicrosoftAuthClient> _logger;

    public MicrosoftAuthClient(HttpClient http, IOptions<MicrosoftOptions> options, ILogger<MicrosoftAuthClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    private string Tenant => string.IsNullOrWhiteSpace(_options.TenantId) ? "common" : _options.TenantId;

    public string BuildAuthUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _options.RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = string.Join(" ", _options.Scopes),
            ["state"] = state,
        };
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{_options.AuthorizationEndpoint.Replace("{tenant}", Tenant)}?{qs}";
    }

    public async Task<MicrosoftTokenResponseDto?> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        var endpoint = _options.TokenEndpoint.Replace("{tenant}", Tenant);
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["grant_type"] = "authorization_code",
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _http.PostAsync(endpoint, new FormUrlEncodedContent(body), ct);
                var content = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<MicrosoftTokenResponseDto>(content);

                // A rejected code (bad, used or expired) will not get better by retrying.
                _logger.LogWarning("Microsoft token exchange failed ({Status}), attempt {Attempt}: {Body}", (int)response.StatusCode, attempt, content);
                if ((int)response.StatusCode is >= 400 and < 500) return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Network error talking to Microsoft, attempt {Attempt}", attempt);
                if (attempt == 3) throw;
            }
            await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
        }
        return null;
    }

    public async Task<MicrosoftUserInfoDto?> GetUserInfoAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Microsoft Graph profile request failed: {Status} {Body}",
                (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            return null;
        }
        return await response.Content.ReadFromJsonAsync<MicrosoftUserInfoDto>(cancellationToken: ct);
    }
}
