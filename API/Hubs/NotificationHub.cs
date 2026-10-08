using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TranslationBackend.API.Filters;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;

namespace TranslationBackend.API.Hubs
{
    /// <summary>
    /// Real-time fan-out. Events: "EmployeeChanged" (every client reloads the employee screen), "ReceiveNotification"
    /// (a toast for one user, or for the SuperUser group) and "JobChanged" (progress of one job, sent to its
    /// owner). The JWT travels as ?access_token= on the WebSocket upgrade (see Program.cs), and ActiveUserMiddleware
    /// also guards the negotiate request, so a deactivated user cannot connect.
    /// </summary>
    [Authorize]
    public class NotificationHub : Hub
    {
        public const string SuperUsersGroup = "SuperUsers";

        public override async Task OnConnectedAsync()
        {
            var role = Context.User?.FindFirstValue(RequireRoleAttribute.ClaimType);
            if (string.Equals(role, "SuperUser", StringComparison.Ordinal))
                await Groups.AddToGroupAsync(Context.ConnectionId, SuperUsersGroup);

            await base.OnConnectedAsync();
        }
    }

    /// <summary>Addresses SignalR users by their Identity id (the NameIdentifier claim) instead of the user name.</summary>
    public class NameIdentifierUserIdProvider : IUserIdProvider
    {
        public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    public class SignalRNotifier : IRealtimeNotifier
    {
        private readonly IHubContext<NotificationHub> _hub;
        private readonly ILogger<SignalRNotifier> _logger;

        public SignalRNotifier(IHubContext<NotificationHub> hub, ILogger<SignalRNotifier> logger)
        {
            _hub = hub;
            _logger = logger;
        }

        public Task EmployeeChangedAsync(string action, string employeeId)
            => Safe(() => _hub.Clients.All.SendAsync("EmployeeChanged", new { action, employeeId }));

        public Task NotifyAdminsAsync(RealtimeNotificationDto notification)
            => Safe(() => _hub.Clients.Group(NotificationHub.SuperUsersGroup)
                .SendAsync("ReceiveNotification", notification));

        public Task NotifyUserAsync(string userId, RealtimeNotificationDto notification)
            => Safe(() => _hub.Clients.User(userId).SendAsync("ReceiveNotification", notification));

        public Task JobChangedAsync(string userId, JobDto job)
            => Safe(() => _hub.Clients.User(userId).SendAsync("JobChanged", job));

        /// <summary>A failed push must never fail the operation that caused it.</summary>
        private async Task Safe(Func<Task> send)
        {
            try
            {
                await send();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not push a real-time update");
            }
        }
    }
}
