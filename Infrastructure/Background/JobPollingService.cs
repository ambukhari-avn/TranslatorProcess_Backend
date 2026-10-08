using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Utils;

namespace TranslationBackend.Infrastructure.Background;

/// <summary>
/// Follows the jobs that are Queued or Running: every few seconds it asks the extraction service for each one and
/// stores the answer (the browser then reads it from this backend, never from the service). When the service has been
/// unreachable for longer than Extraction:UnavailableMinutes the unfinished jobs are marked Failed, so none hangs in
/// "Running" forever. It also settles jobs left unfinished by a restart, because it reads the service's answer, not memory.
/// </summary>
public class JobPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ExtractionOptions _options;
    private readonly ILogger<JobPollingService> _logger;
    private DateTime? _unreachableSince;

    public JobPollingService(IServiceScopeFactory scopes, IOptions<ExtractionOptions> options, ILogger<JobPollingService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job polling failed; will retry");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One pass over the active jobs. Public so tests can run a pass without waiting for the timer.</summary>
    public async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IJobService>();

        var ids = await jobs.ActiveJobIdsAsync(ct);
        if (ids.Count == 0)
        {
            _unreachableSince = null;
            return;
        }

        foreach (var id in ids)
        {
            try
            {
                await jobs.RefreshAsync(id, ct);
                _unreachableSince = null;
            }
            catch (ExtractionUnavailableException ex)
            {
                _unreachableSince ??= DateTimeHelper.Now;
                var down = DateTimeHelper.Now - _unreachableSince.Value;
                _logger.LogWarning("Extraction service unreachable for {Seconds:N0}s: {Message}", down.TotalSeconds, ex.Message);

                if (down > TimeSpan.FromMinutes(Math.Max(1, _options.UnavailableMinutes)))
                {
                    foreach (var stuck in ids)
                        await jobs.FailAsync(stuck, "The translation service stopped responding. Upload the file again in a few minutes.", ct);
                    _unreachableSince = null;
                }
                return;     // the service is down for every job; try again at the next tick
            }
        }
    }
}
