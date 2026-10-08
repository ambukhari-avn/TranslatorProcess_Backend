using System.ComponentModel.DataAnnotations;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.Application.DTOs
{
    /// <summary>What a user sees about a translation. Files are never exposed by path, only by the download endpoints.</summary>
    public class JobDto
    {
        public Guid Id { get; set; }
        public string OriginalName { get; set; } = string.Empty;
        public string InputType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Language { get; set; } = "ru";
        public bool Verify { get; set; }
        public JobStatus Status { get; set; }
        public int Progress { get; set; }
        public string? Stage { get; set; }
        public int CriticalCount { get; set; }
        public int ModerateCount { get; set; }
        public bool ReviewRequired { get; set; }
        public double? DocumentScore { get; set; }
        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        /// <summary>True while the translated file can still be downloaded.</summary>
        public bool CanDownload { get; set; }
        public bool CanDownloadReview { get; set; }
        /// <summary>The files were deleted after the retention period.</summary>
        public bool FilesExpired { get; set; }
    }

    /// <summary>A job in the history view (Developer only): the job plus who ran it.</summary>
    public class JobHistoryDto : JobDto
    {
        public string UserId { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
    }

    public class JobFilterDto
    {
        [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
        [Range(1, 200)] public int PageSize { get; set; } = 20;
        public JobStatus? Status { get; set; }
        /// <summary>Free text across the file name (and, in the history, the user's name and email).</summary>
        public string? Search { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        /// <summary>History only: restrict to one user.</summary>
        public string? UserId { get; set; }
    }

    public enum JobFileKind
    {
        Output,
        Review,
    }

    /// <summary>A file ready to be sent to the browser.</summary>
    public record JobFileResult(string FullPath, string DownloadName, string ContentType);
}
