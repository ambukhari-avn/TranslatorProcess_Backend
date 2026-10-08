using TranslationBackend.Application.DTOs;

namespace TranslationBackend.Application.Interface.IService
{
    /// <summary>System-wide audit log. Call <see cref="LogAsync"/> wherever something worth recording happens.</summary>
    public interface IAuditService
    {
        Task LogAsync(
            string action,
            string? userId = null,
            string? userName = null,
            string? entityType = null,
            string? entityId = null,
            CancellationToken ct = default);

        Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditLogFilterDto filter, CancellationToken ct = default);
    }

    public interface IEmployeeService
    {
        Task<PagedResult<EmployeeDto>> GetPagedAsync(EmployeeFilterDto filter, CancellationToken ct = default);
        Task<EmployeeDto?> GetByIdAsync(string id, CancellationToken ct = default);
        Task<EmployeeDto?> SetActiveAsync(string id, bool isActive, CancellationToken ct = default);
    }

    /// <summary>Pushes live updates to the browsers (implemented over SignalR in the API layer).</summary>
    public interface IRealtimeNotifier
    {
        /// <summary>Tells every open employee screen to reload.</summary>
        Task EmployeeChangedAsync(string action, string employeeId);

        /// <summary>A toast for every SuperUser (Developer).</summary>
        Task NotifyAdminsAsync(RealtimeNotificationDto notification);

        /// <summary>A toast for one user.</summary>
        Task NotifyUserAsync(string userId, RealtimeNotificationDto notification);

        /// <summary>Progress / status of one job, for the screens of its owner.</summary>
        Task JobChangedAsync(string userId, JobDto job);
    }
}
