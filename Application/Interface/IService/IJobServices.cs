using TranslationBackend.Application.DTOs;
using TranslationBackend.Domain.Model;

namespace TranslationBackend.Application.Interface.IService
{
    /// <summary>The caller of a job operation: who they are and whether they may see everyone's jobs.</summary>
    public record JobCaller(string UserId, string UserName, bool CanSeeAll);

    public interface IJobService
    {
        /// <summary>Validate and store the upload, create the job and start it on the extraction service.</summary>
        Task<JobDto> CreateAsync(JobCaller caller, string fileName, long length, Stream content, string language, bool verify, CancellationToken ct = default);

        Task<PagedResult<JobDto>> ListMineAsync(string userId, JobFilterDto filter, CancellationToken ct = default);

        /// <summary>Developer view: every user's jobs.</summary>
        Task<PagedResult<JobHistoryDto>> HistoryAsync(JobFilterDto filter, CancellationToken ct = default);

        /// <summary>The job, or null when it does not exist or the caller may not see it.</summary>
        Task<JobDto?> GetAsync(JobCaller caller, Guid id, CancellationToken ct = default);

        /// <summary>Cancel a queued or running job. Returns null when the caller may not see it.</summary>
        Task<JobDto?> CancelAsync(JobCaller caller, Guid id, CancellationToken ct = default);

        /// <summary>The translated or review file. Null when the job is not visible; throws <see cref="JobFileException"/> otherwise.</summary>
        Task<JobFileResult?> GetFileAsync(JobCaller caller, Guid id, JobFileKind kind, CancellationToken ct = default);

        /// <summary>Asks the extraction service about a job and stores the answer. Used by the background poller.</summary>
        Task<bool> RefreshAsync(Guid id, CancellationToken ct = default);

        /// <summary>Jobs that are still Queued or Running.</summary>
        Task<IReadOnlyList<Guid>> ActiveJobIdsAsync(CancellationToken ct = default);

        /// <summary>Marks an unfinished job Failed (used when the extraction service has been unreachable too long).</summary>
        Task FailAsync(Guid id, string reason, CancellationToken ct = default);

        /// <summary>Deletes the files of jobs finished more than <paramref name="retentionDays"/> ago. Returns how many.</summary>
        Task<int> DeleteExpiredFilesAsync(int retentionDays, CancellationToken ct = default);
    }

    /// <summary>A request the service refuses, with a code the API turns into a status and a message.</summary>
    public class JobException : Exception
    {
        public int StatusCode { get; }
        public string Code { get; }

        public JobException(int statusCode, string code, string message) : base(message)
        {
            StatusCode = statusCode;
            Code = code;
        }
    }

    public class JobFileException : JobException
    {
        public JobFileException(int statusCode, string code, string message) : base(statusCode, code, message) { }
    }

    public enum ExtractionJobStatus { Queued, Running, Done, Failed, Cancelled }

    /// <summary>What the extraction service answers for one job.</summary>
    public record ExtractionJobState(
        string JobId,
        ExtractionJobStatus Status,
        int Progress,
        string? Stage,
        int Critical,
        int Moderate,
        double? DocumentScore,
        bool ReviewRequired,
        string? Error,
        string? OutputFile,
        string? ReviewFile,
        string? ReportFile,
        DateTime? StartedAt,
        DateTime? FinishedAt);

    public record ExtractionStartRequest(string JobId, string InputPath, string OutputDir, string Language, bool Verify);

    /// <summary>The extraction service cannot be reached or answered with an error.</summary>
    public class ExtractionUnavailableException : Exception
    {
        public ExtractionUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public interface IExtractionClient
    {
        /// <summary>Starts (or re-reads, for a repeated id) a job. Throws <see cref="ExtractionUnavailableException"/>.</summary>
        Task<ExtractionJobState> StartAsync(ExtractionStartRequest request, CancellationToken ct = default);

        /// <summary>The job's state, or null when the service does not know the job.</summary>
        Task<ExtractionJobState?> GetAsync(string jobId, CancellationToken ct = default);

        /// <summary>Asks the service to cancel; false when the job has already finished or is unknown.</summary>
        Task<bool> CancelAsync(string jobId, CancellationToken ct = default);

        /// <summary>True when the service answers its health check.</summary>
        Task<bool> IsHealthyAsync(CancellationToken ct = default);
    }

    /// <summary>The folder the backend and the extraction service share (paths in the database are relative to it).</summary>
    public interface IFileStorage
    {
        /// <summary>Writes the upload to &lt;root&gt;/&lt;jobId&gt;/input/source.&lt;ext&gt; and returns that path relative to the root.</summary>
        Task<string> SaveUploadAsync(Guid jobId, string extension, Stream content, CancellationToken ct = default);

        /// <summary>Folder the extraction service writes to (relative), e.g. "&lt;jobId&gt;/output".</summary>
        string OutputDirectory(Guid jobId);

        /// <summary>Absolute path of a stored relative path, or null when it would leave the storage root.</summary>
        string? Resolve(string relativePath);

        /// <summary>Relative form of a path the extraction service reported, or null when it is outside the root.</summary>
        string? ToRelative(string? path);

        void DeleteJobFolder(Guid jobId);
    }
}
