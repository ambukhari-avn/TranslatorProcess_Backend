using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;

namespace TranslationBackend.API.Middleware
{
    /// <summary>
    /// Kill switch for live sessions. On every authenticated request it re-reads the user and answers 401 with
    /// <c>deactivated: true</c> when the account no longer exists or is deactivated, so a
    /// token cannot outlive the permission it was issued for. The frontend interceptor turns that into a logout
    /// and the "no access" screen. Anonymous requests and the sign-in endpoints pass straight through.
    /// </summary>
    public class ActiveUserMiddleware
    {
        private static readonly string[] BypassPrefixes = ["/api/auth/", "/api/health", "/swagger"];

        private readonly RequestDelegate _next;

        public ActiveUserMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, UserManager<Users> userManager)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (BypassPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                await _next(context);
                return;
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                await Reject(context, "Your sign-in session is stale (the linked account no longer exists). Sign out and sign in again.");
                return;
            }
            if (!user.IsActive)
            {
                await Reject(context, "Your account has been deactivated. Contact a Developer to regain access.");
                return;
            }
            await _next(context);
        }

        private static Task Reject(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(new { message, deactivated = true });
        }
    }
}
