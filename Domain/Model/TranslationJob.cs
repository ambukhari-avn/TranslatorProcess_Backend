using System.ComponentModel.DataAnnotations;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.Domain.Model
{
    /// <summary>
    /// One translation request. The files stay on disk under the storage root; the row keeps their paths
    /// (relative to that root, so the folder can move) and what the extraction service reported.
    /// The Id is also the job id used with the extraction service.
    /// </summary>
    public class TranslationJob
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public Users? User { get; set; }

        [Required, MaxLength(260)]
        public string OriginalName { get; set; } = string.Empty;

        /// <summary>"pdf" or "docx".</summary>
        [Required, MaxLength(8)]
        public string InputType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        /// <summary>"ru" or "kk".</summary>
        [Required, MaxLength(8)]
        public string Language { get; set; } = "ru";

        /// <summary>Second-model verification and repair pass.</summary>
        public bool Verify { get; set; } = true;

        [Required, MaxLength(500)]
        public string InputPath { get; set; } = string.Empty;
        [MaxLength(500)] public string? OutputPath { get; set; }
        [MaxLength(500)] public string? ReviewPath { get; set; }
        [MaxLength(500)] public string? ReportPath { get; set; }

        public JobStatus Status { get; set; } = JobStatus.Queued;
        public int Progress { get; set; }
        [MaxLength(64)] public string? Stage { get; set; }

        public int CriticalCount { get; set; }
        public int ModerateCount { get; set; }
        public bool ReviewRequired { get; set; }
        public double? DocumentScore { get; set; }

        [MaxLength(1000)] public string? Error { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        /// <summary>Set when the retention period passed and the files were deleted (the row is kept for the history).</summary>
        public DateTime? FilesDeletedAt { get; set; }

        public bool IsFinished => Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled;
    }
}
