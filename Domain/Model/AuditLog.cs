using System.ComponentModel.DataAnnotations;

namespace TranslationBackend.Domain.Model
{
    /// <summary>
    /// System-wide activity log: sign-ins, approvals and rejections, role and status changes, uploads, finished and
    /// cancelled translations. The text is written at the call site so the audit view reads naturally.
    /// </summary>
    public class AuditLog
    {
        [Key]
        public long Id { get; set; }

        [Required, MaxLength(500)]
        public string Action { get; set; } = string.Empty;

        /// <summary>User who performed the action; null for anonymous or system events.</summary>
        public string? UserId { get; set; }

        /// <summary>User name at the time, so the row stays readable if the user is later renamed or deleted.</summary>
        [MaxLength(256)]
        public string? UserName { get; set; }

        /// <summary>"Auth", "Employee", "Job", ...</summary>
        [MaxLength(64)]
        public string? EntityType { get; set; }

        [MaxLength(64)]
        public string? EntityId { get; set; }

        public DateTime Timestamp { get; set; }

        public Users? User { get; set; }
    }
}
