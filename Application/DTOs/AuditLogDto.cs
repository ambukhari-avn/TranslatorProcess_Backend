namespace TranslationBackend.Application.DTOs
{
    public class AuditLogDto
    {
        public long Id { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AuditLogFilterDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string? Search { get; set; }
        /// <summary>"id", "action", "username" or "timestamp" (default).</summary>
        public string? SortBy { get; set; }
        /// <summary>"asc" or "desc" (default).</summary>
        public string? SortOrder { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}
