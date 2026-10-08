using System.IO.Compression;
using Microsoft.Extensions.Options;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IRepo;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Utils;

namespace TranslationBackend.Application.Services
{
    /// <summary>
    /// The translation jobs: validate and store an upload, start it on the extraction service, keep its progress in the
    /// database, and hand the result files to the right person. The service owns every rule about who may see what.
    /// </summary>
    public class JobService : IJobService
    {
        private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        private static readonly string[] Languages = ["ru", "kk"];

        private readonly IJobRepo _repo;
        private readonly IExtractionClient _extraction;
        private readonly IFileStorage _storage;
        private readonly IAuditService _audit;
        private readonly IRealtimeNotifier _notifier;
        private readonly JobOptions _options;
        private readonly ILogger<JobService> _logger;

        public JobService(IJobRepo repo, IExtractionClient extraction, IFileStorage storage, IAuditService audit,
            IRealtimeNotifier notifier, IOptions<JobOptions> options, ILogger<JobService> logger)
        {
            _repo = repo;
            _extraction = extraction;
            _storage = storage;
            _audit = audit;
            _notifier = notifier;
            _options = options.Value;
            _logger = logger;
        }

        // ------------------------------------------------------------------ create
        public async Task<JobDto> CreateAsync(JobCaller caller, string fileName, long length, Stream content,
            string language, bool verify, CancellationToken ct = default)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension is not (".pdf" or ".docx"))
                throw new JobException(400, "UNSUPPORTED_FILE_TYPE", "Only .pdf and .docx files can be translated.");
            if (length <= 0)
                throw new JobException(400, "EMPTY_FILE", "The file is empty.");
            if (length > _options.MaxUploadBytes)
                throw new JobException(413, "FILE_TOO_LARGE", $"The file is larger than {_options.MaxUploadBytes / (1024 * 1024)} MB.");
            language = (language ?? "ru").Trim().ToLowerInvariant();
            if (!Languages.Contains(language))
                throw new JobException(400, "UNSUPPORTED_LANGUAGE", "The language must be 'ru' or 'kk'.");
            if (await _repo.CountActiveForUserAsync(caller.UserId, ct) >= _options.MaxActivePerUser)
                throw new JobException(429, "TOO_MANY_ACTIVE_JOBS",
                    $"You already have {_options.MaxActivePerUser} translations in progress. Wait for one to finish.");

            var id = Guid.NewGuid();
            string relativePath;
            try
            {
                relativePath = await _storage.SaveUploadAsync(id, extension, content, ct);
                CheckFileContent(_storage.Resolve(relativePath)!, extension);
            }
            catch
            {
                _storage.DeleteJobFolder(id);       // nothing is kept of a refused upload
                throw;
            }

            var job = new TranslationJob
            {
                Id = id,
                UserId = caller.UserId,
                OriginalName = Truncate(Path.GetFileName(fileName), 260),
                InputType = extension.TrimStart('.'),
                SizeBytes = length,
                Language = language,
                Verify = verify,
                InputPath = relativePath,
                Status = JobStatus.Queued,
                CreatedAt = DateTimeHelper.Now,
            };
            await _repo.AddAsync(job, ct);

            try
            {
                var state = await _extraction.StartAsync(
                    new ExtractionStartRequest(id.ToString("D"), relativePath, _storage.OutputDirectory(id), language, verify), ct);
                Apply(job, state);
                await _repo.SaveAsync(ct);
            }
            catch (ExtractionUnavailableException ex)
            {
                _logger.LogError(ex, "Could not start job {JobId} on the extraction service", id);
                job.Status = JobStatus.Failed;
                job.Error = "The translation service is not available. Try again in a few minutes.";
                job.FinishedAt = DateTimeHelper.Now;
                await _repo.SaveAsync(ct);
                throw new JobException(503, "SERVICE_UNAVAILABLE", job.Error);
            }

            await _audit.LogAsync($"Started translation of {job.OriginalName} ({job.Language})", caller.UserId, caller.UserName, "Job", id.ToString(), ct);
            return ToDto(job);
        }

        // ------------------------------------------------------------------ read
        public async Task<PagedResult<JobDto>> ListMineAsync(string userId, JobFilterDto filter, CancellationToken ct = default)
        {
            var (items, total) = await _repo.ListByUserAsync(userId, filter, ct);
            return new PagedResult<JobDto>
            {
                Items = items.Select(ToDto).ToList(),
                Page = Math.Max(1, filter.Page),
                PageSize = Math.Clamp(filter.PageSize, 1, 200),
                TotalCount = total,
            };
        }

        public async Task<PagedResult<JobHistoryDto>> HistoryAsync(JobFilterDto filter, CancellationToken ct = default)
        {
            var (items, total) = await _repo.ListAllAsync(filter, ct);
            return new PagedResult<JobHistoryDto>
            {
                Items = items.Select(ToHistoryDto).ToList(),
                Page = Math.Max(1, filter.Page),
                PageSize = Math.Clamp(filter.PageSize, 1, 200),
                TotalCount = total,
            };
        }

        public async Task<JobDto?> GetAsync(JobCaller caller, Guid id, CancellationToken ct = default)
        {
            var job = await _repo.GetAsync(id, ct);
            return job != null && CanSee(caller, job) ? ToDto(job) : null;
        }

        // ------------------------------------------------------------------ cancel
        public async Task<JobDto?> CancelAsync(JobCaller caller, Guid id, CancellationToken ct = default)
        {
            var job = await _repo.GetAsync(id, ct);
            if (job == null || !CanSee(caller, job)) return null;
            if (job.IsFinished)
                throw new JobException(409, "JOB_FINISHED", $"The translation already finished ({job.Status}).");

            try
            {
                await _extraction.CancelAsync(id.ToString("D"), ct);
                var state = await _extraction.GetAsync(id.ToString("D"), ct);
                if (state != null) Apply(job, state);
            }
            catch (ExtractionUnavailableException)
            {
                throw new JobException(503, "SERVICE_UNAVAILABLE", "The translation service is not available, so the job could not be cancelled.");
            }

            await _repo.SaveAsync(ct);
            await _audit.LogAsync($"Cancelled translation of {job.OriginalName}", caller.UserId, caller.UserName, "Job", id.ToString(), ct);
            await _notifier.JobChangedAsync(job.UserId, ToDto(job));
            return ToDto(job);
        }

        // ------------------------------------------------------------------ files
        public async Task<JobFileResult?> GetFileAsync(JobCaller caller, Guid id, JobFileKind kind, CancellationToken ct = default)
        {
            var job = await _repo.GetAsync(id, ct);
            if (job == null || !CanSee(caller, job)) return null;

            if (job.FilesDeletedAt != null)
                throw new JobFileException(410, "FILES_EXPIRED", "The files of this translation were deleted after the retention period.");
            if (job.Status != JobStatus.Done)
                throw new JobFileException(409, "NOT_READY", "The translation is not finished yet.");

            var relative = kind == JobFileKind.Output ? job.OutputPath : job.ReviewPath;
            var full = relative == null ? null : _storage.Resolve(relative);
            if (full == null || !File.Exists(full))
                throw new JobFileException(410, "FILE_MISSING", "The file is no longer available.");

            var stem = SafeFileName(Path.GetFileNameWithoutExtension(job.OriginalName));
            var name = kind == JobFileKind.Output ? $"{stem}_{job.Language}.docx" : $"{stem}_{job.Language}_review.docx";
            return new JobFileResult(full, name, DocxContentType);
        }

        // ------------------------------------------------------------------ background support
        public Task<IReadOnlyList<Guid>> ActiveJobIdsAsync(CancellationToken ct = default) => _repo.ActiveIdsAsync(ct);

        public async Task<bool> RefreshAsync(Guid id, CancellationToken ct = default)
        {
            var job = await _repo.GetAsync(id, ct);
            if (job == null || job.IsFinished) return false;

            var state = await _extraction.GetAsync(id.ToString("D"), ct);     // may throw ExtractionUnavailableException
            var wasFinished = job.IsFinished;
            bool changed;
            if (state == null)
            {
                // The service does not know this job (it was restarted, or never received it).
                if (DateTimeHelper.Now - job.CreatedAt < TimeSpan.FromMinutes(2)) return false;
                MarkFailed(job, "The translation service no longer knows this job (it may have restarted). Upload the file again.");
                changed = true;
            }
            else
            {
                changed = Apply(job, state);
            }

            if (!changed) return false;
            await _repo.SaveAsync(ct);
            await _notifier.JobChangedAsync(job.UserId, ToDto(job));
            if (!wasFinished && job.IsFinished)
                await AnnounceFinishedAsync(job, ct);
            return true;
        }

        public async Task FailAsync(Guid id, string reason, CancellationToken ct = default)
        {
            var job = await _repo.GetAsync(id, ct);
            if (job == null || job.IsFinished) return;
            MarkFailed(job, reason);
            await _repo.SaveAsync(ct);
            await _notifier.JobChangedAsync(job.UserId, ToDto(job));
            await AnnounceFinishedAsync(job, ct);
        }

        public async Task<int> DeleteExpiredFilesAsync(int retentionDays, CancellationToken ct = default)
        {
            var cutoff = DateTimeHelper.Now.AddDays(-Math.Max(1, retentionDays));
            var expired = await _repo.FinishedBeforeAsync(cutoff, ct);
            foreach (var job in expired)
            {
                try
                {
                    _storage.DeleteJobFolder(job.Id);
                    job.FilesDeletedAt = DateTimeHelper.Now;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete the files of job {JobId}", job.Id);
                }
            }
            if (expired.Count > 0) await _repo.SaveAsync(ct);
            return expired.Count(j => j.FilesDeletedAt != null);
        }

        // ------------------------------------------------------------------ helpers
        private async Task AnnounceFinishedAsync(TranslationJob job, CancellationToken ct)
        {
            var (title, message) = job.Status switch
            {
                JobStatus.Done => ("Translation ready",
                    job.ReviewRequired ? $"{job.OriginalName} is translated; {job.CriticalCount + job.ModerateCount} row(s) need review."
                                       : $"{job.OriginalName} is translated."),
                JobStatus.Cancelled => ("Translation cancelled", $"{job.OriginalName} was cancelled."),
                _ => ("Translation failed", $"{job.OriginalName} could not be translated: {job.Error}"),
            };
            await _audit.LogAsync($"Translation of {job.OriginalName} finished: {job.Status}", job.UserId, null, "Job", job.Id.ToString(), ct);
            await _notifier.NotifyUserAsync(job.UserId, new RealtimeNotificationDto
            {
                Type = $"Job{job.Status}",
                Title = title,
                Message = message,
                EntityType = "Job",
                EntityId = job.Id.ToString(),
            });
        }

        private void MarkFailed(TranslationJob job, string reason)
        {
            job.Status = JobStatus.Failed;
            job.Error = Truncate(reason, 1000);
            job.FinishedAt = DateTimeHelper.Now;
        }

        /// <summary>Copies what the extraction service reports into the job. Returns whether anything changed.</summary>
        private bool Apply(TranslationJob job, ExtractionJobState state)
        {
            var before = (job.Status, job.Progress, job.Stage, job.CriticalCount, job.ModerateCount, job.Error, job.OutputPath);

            job.Status = state.Status switch
            {
                ExtractionJobStatus.Queued => JobStatus.Queued,
                ExtractionJobStatus.Running => JobStatus.Running,
                ExtractionJobStatus.Done => JobStatus.Done,
                ExtractionJobStatus.Cancelled => JobStatus.Cancelled,
                _ => JobStatus.Failed,
            };
            job.Progress = Math.Clamp(state.Progress, 0, 100);
            job.Stage = Truncate(state.Stage, 64);
            job.CriticalCount = state.Critical;
            job.ModerateCount = state.Moderate;
            job.DocumentScore = state.DocumentScore;
            job.ReviewRequired = state.ReviewRequired;
            job.Error = state.Error == null ? null : Truncate(state.Error, 1000);
            job.StartedAt ??= state.StartedAt;

            if (job.Status == JobStatus.Done)
            {
                job.OutputPath = _storage.ToRelative(state.OutputFile);
                job.ReviewPath = _storage.ToRelative(state.ReviewFile);
                job.ReportPath = _storage.ToRelative(state.ReportFile);
                if (job.OutputPath == null)
                    MarkFailed(job, "The translation finished but its file was not found in the shared storage folder. Check that Storage:Root and EXTRACTION_FILES_ROOT are the same folder.");
            }
            if (job.IsFinished)
                job.FinishedAt ??= state.FinishedAt ?? DateTimeHelper.Now;

            return before != (job.Status, job.Progress, job.Stage, job.CriticalCount, job.ModerateCount, job.Error, job.OutputPath);
        }

        private static bool CanSee(JobCaller caller, TranslationJob job)
            => caller.CanSeeAll || string.Equals(job.UserId, caller.UserId, StringComparison.Ordinal);

        /// <summary>The extension alone proves nothing: check that the content really is a PDF or a Word file.</summary>
        private static void CheckFileContent(string fullPath, string extension)
        {
            var invalid = new JobException(400, "INVALID_FILE",
                extension == ".pdf" ? "The file is not a valid PDF." : "The file is not a valid Word (.docx) document.");
            try
            {
                using var stream = File.OpenRead(fullPath);
                var head = new byte[5];
                var read = stream.Read(head, 0, head.Length);
                if (extension == ".pdf")
                {
                    if (read < 5 || head[0] != '%' || head[1] != 'P' || head[2] != 'D' || head[3] != 'F' || head[4] != '-') throw invalid;
                    return;
                }
                if (read < 4 || head[0] != 'P' || head[1] != 'K') throw invalid;
                stream.Position = 0;
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                if (zip.GetEntry("word/document.xml") == null) throw invalid;
            }
            catch (InvalidDataException)
            {
                throw invalid;
            }
        }

        private static string SafeFileName(string name)
        {
            var cleaned = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
            return string.IsNullOrEmpty(cleaned) ? "document" : cleaned;
        }

        private static string Truncate(string? value, int max)
            => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];

        private static JobDto ToDto(TranslationJob job) => Fill(new JobDto(), job);

        private static JobHistoryDto ToHistoryDto(TranslationJob job)
        {
            var dto = Fill(new JobHistoryDto(), job);
            dto.UserId = job.UserId;
            dto.UserName = job.User?.Name;
            dto.UserEmail = job.User?.Email;
            return dto;
        }

        private static T Fill<T>(T dto, TranslationJob job) where T : JobDto
        {
            dto.Id = job.Id;
            dto.OriginalName = job.OriginalName;
            dto.InputType = job.InputType;
            dto.SizeBytes = job.SizeBytes;
            dto.Language = job.Language;
            dto.Verify = job.Verify;
            dto.Status = job.Status;
            dto.Progress = job.Progress;
            dto.Stage = job.Stage;
            dto.CriticalCount = job.CriticalCount;
            dto.ModerateCount = job.ModerateCount;
            dto.ReviewRequired = job.ReviewRequired;
            dto.DocumentScore = job.DocumentScore;
            dto.Error = job.Error;
            dto.CreatedAt = job.CreatedAt;
            dto.StartedAt = job.StartedAt;
            dto.FinishedAt = job.FinishedAt;
            dto.FilesExpired = job.FilesDeletedAt != null;
            dto.CanDownload = job.Status == JobStatus.Done && job.OutputPath != null && job.FilesDeletedAt == null;
            dto.CanDownloadReview = job.Status == JobStatus.Done && job.ReviewPath != null && job.FilesDeletedAt == null;
            return dto;
        }
    }
}
