using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Utils;

namespace TranslationBackend.Infrastructure.External;

/// <summary>Talks to the extraction (FastAPI) service: start a job, read its progress, cancel it.</summary>
public class ExtractionClient : IExtractionClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;
    private readonly ILogger<ExtractionClient> _logger;

    public ExtractionClient(HttpClient http, IOptions<ExtractionOptions> options, ILogger<ExtractionClient> logger)
    {
        _http = http;
        _logger = logger;
        var o = options.Value;
        _http.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, o.TimeoutSeconds));
        if (!string.IsNullOrEmpty(o.ApiKey))
            _http.DefaultRequestHeaders.Add("X-API-Key", o.ApiKey);
    }

    public async Task<ExtractionJobState> StartAsync(ExtractionStartRequest request, CancellationToken ct = default)
    {
        var body = new
        {
            jobId = request.JobId,
            inputPath = request.InputPath,
            outputDir = request.OutputDir,
            options = new { language = request.Language, verify = request.Verify },
        };
        using var content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var response = await SendAsync(() => _http.PostAsync("jobs", content, ct));
        await EnsureSuccessAsync(response, ct);
        return Map(await ReadAsync(response, ct));
    }

    public async Task<ExtractionJobState?> GetAsync(string jobId, CancellationToken ct = default)
    {
        using var response = await SendAsync(() => _http.GetAsync($"jobs/{Uri.EscapeDataString(jobId)}", ct));
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct);
        return Map(await ReadAsync(response, ct));
    }

    public async Task<bool> CancelAsync(string jobId, CancellationToken ct = default)
    {
        using var response = await SendAsync(() => _http.PostAsync($"jobs/{Uri.EscapeDataString(jobId)}/cancel", null, ct));
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict) return false;
        await EnsureSuccessAsync(response, ct);
        return true;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ExtractionUnavailableException("The translation service cannot be reached.", ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("Extraction service answered {Status}: {Body}", (int)response.StatusCode, text);
        var message = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "The translation service rejected the backend's API key."
            : $"The translation service returned an error ({(int)response.StatusCode}).";
        throw new ExtractionUnavailableException(message);
    }

    private static async Task<ApiJob> ReadAsync(HttpResponseMessage response, CancellationToken ct)
        => await response.Content.ReadFromJsonAsync<ApiJob>(Json, ct)
           ?? throw new ExtractionUnavailableException("The translation service sent an empty answer.");

    private static ExtractionJobState Map(ApiJob j) => new(
        j.JobId,
        Enum.TryParse<ExtractionJobStatus>(j.Status, ignoreCase: true, out var status) ? status : ExtractionJobStatus.Failed,
        j.Progress,
        j.Stage,
        j.Critical,
        j.Moderate,
        j.DocumentScore,
        j.ReviewRequired,
        j.Error,
        j.Files?.Output,
        j.Files?.Review,
        j.Files?.Report,
        j.StartedAt is { } s ? DateTimeHelper.ToPakistanTime(s.UtcDateTime) : null,
        j.FinishedAt is { } f ? DateTimeHelper.ToPakistanTime(f.UtcDateTime) : null);

    // The extraction service's JSON (camelCase).
    private class ApiJob
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; }
        public string? Stage { get; set; }
        public int Critical { get; set; }
        public int Moderate { get; set; }
        public double? DocumentScore { get; set; }
        public bool ReviewRequired { get; set; }
        public string? Error { get; set; }
        public ApiFiles? Files { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
    }

    private class ApiFiles
    {
        public string? Output { get; set; }
        public string? Review { get; set; }
        public string? Report { get; set; }
        public string? Log { get; set; }
    }
}
