using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Application.Interface.IService;

namespace TranslationBackend.Infrastructure.Background;

/// <summary>
/// Deletes the files of translations finished more than Jobs:RetentionDays ago (the client documents are confidential
/// and the disk is finite). The history rows stay, flagged as expired.
/// </summary>
public class FileCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly JobOptions _options;
    private readonly ILogger<FileCleanupService> _logger;

    public FileCleanupService(IServiceScopeFactory scopes, IOptions<JobOptions> options, ILogger<FileCleanupService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.CleanupIntervalHours));
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);     // let the app finish starting first
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var deleted = await scope.ServiceProvider.GetRequiredService<IJobService>()
                        .DeleteExpiredFilesAsync(_options.RetentionDays, stoppingToken);
                    if (deleted > 0) _logger.LogInformation("Deleted the files of {Count} expired translation(s)", deleted);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "File cleanup failed; will retry");
                }
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
