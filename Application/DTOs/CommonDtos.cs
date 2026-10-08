using TranslationBackend.Utils;

namespace TranslationBackend.Application.DTOs
{
    public class PagedResult<T>
    {
        public IEnumerable<T> Items { get; init; } = [];
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int TotalCount { get; init; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }

    /// <summary>A message a person should see, pushed over SignalR (shown as a toast).</summary>
    public class RealtimeNotificationDto
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTimeHelper.Now;
    }
}
