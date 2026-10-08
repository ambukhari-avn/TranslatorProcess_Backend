using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Infrastructure.Data;
using TranslationBackend.Infrastructure.Storage;

namespace TranslationBackend.Tests;

/// <summary>A scriptable stand-in for the extraction (FastAPI) service.</summary>
public class FakeExtractionClient : IExtractionClient
{
    public bool Unavailable { get; set; }
    public Dictionary<string, ExtractionJobState> Jobs { get; } = new();
    public List<ExtractionStartRequest> Started { get; } = new();
    public List<string> Cancelled { get; } = new();

    public Task<ExtractionJobState> StartAsync(ExtractionStartRequest request, CancellationToken ct = default)
    {
        if (Unavailable) throw new ExtractionUnavailableException("down");
        Started.Add(request);
        var state = State(request.JobId, ExtractionJobStatus.Queued, 0);
        Jobs[request.JobId] = state;
        return Task.FromResult(state);
    }

    public Task<ExtractionJobState?> GetAsync(string jobId, CancellationToken ct = default)
    {
        if (Unavailable) throw new ExtractionUnavailableException("down");
        return Task.FromResult(Jobs.TryGetValue(jobId, out var s) ? s : null);
    }

    public Task<bool> CancelAsync(string jobId, CancellationToken ct = default)
    {
        if (Unavailable) throw new ExtractionUnavailableException("down");
        Cancelled.Add(jobId);
        if (Jobs.TryGetValue(jobId, out var s)) Jobs[jobId] = s with { Status = ExtractionJobStatus.Cancelled };
        return Task.FromResult(true);
    }

    public Task<bool> IsHealthyAsync(CancellationToken ct = default) => Task.FromResult(!Unavailable);

    public static ExtractionJobState State(string id, ExtractionJobStatus status, int progress, string? stage = null) =>
        new(id, status, progress, stage, 0, 0, null, false, null, null, null, null, null, null);

    public void Finish(string id, string outputFile, string? reviewFile = null, int critical = 0, int moderate = 3) =>
        Jobs[id] = new ExtractionJobState(id, ExtractionJobStatus.Done, 100, "Done", critical, moderate, 95.5, moderate + critical > 0,
            null, outputFile, reviewFile, null, DateTime.Now.AddMinutes(-1), DateTime.Now);
}

public class FakeNotifier : IRealtimeNotifier
{
    public List<string> Events { get; } = new();
    public List<RealtimeNotificationDto> Toasts { get; } = new();

    public Task EmployeeChangedAsync(string action, string employeeId) { Events.Add($"employee:{action}"); return Task.CompletedTask; }
    public Task NotifyAdminsAsync(RealtimeNotificationDto n) { Toasts.Add(n); return Task.CompletedTask; }
    public Task NotifyUserAsync(string userId, RealtimeNotificationDto n) { Toasts.Add(n); return Task.CompletedTask; }
    public Task JobChangedAsync(string userId, JobDto job) { Events.Add($"job:{job.Status}:{job.Progress}"); return Task.CompletedTask; }
}

public class FakeAudit : IAuditService
{
    public List<string> Actions { get; } = new();

    public Task LogAsync(string action, string? userId = null, string? userName = null, string? entityType = null, string? entityId = null, CancellationToken ct = default)
    {
        Actions.Add(action);
        return Task.CompletedTask;
    }

    public Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditLogFilterDto filter, CancellationToken ct = default)
        => Task.FromResult(new PagedResult<AuditLogDto>());
}

public static class TestData
{
    public static AppDbContext NewContext()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public static LocalFileStorage NewStorage(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "tb-tests-" + Guid.NewGuid().ToString("N"));
        return new LocalFileStorage(Options.Create(new StorageOptions { Root = root }));
    }

    public static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF");

    public static byte[] Docx(bool withDocument = true)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(withDocument ? "word/document.xml" : "other.xml");
            using var w = new StreamWriter(entry.Open());
            w.Write("<w:document/>");
        }
        return ms.ToArray();
    }
}
