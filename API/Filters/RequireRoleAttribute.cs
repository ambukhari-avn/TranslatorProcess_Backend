using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.API.Filters
{
    /// <summary>
    /// Lets a request through only when the user's "Role" claim is one of the allowed roles.
    ///   [RequireRole(Role.SuperUser)]                  // Developers only
    ///   [RequireRole(Role.Employee, Role.SuperUser)]   // either role
    /// 401 when not signed in, 403 when signed in with another role.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class RequireRoleAttribute : Attribute, IAuthorizationFilter
    {
        public const string ClaimType = "Role";

        private readonly Role[] _allowed;

        public RequireRoleAttribute(params Role[] allowed)
        {
            _allowed = allowed ?? Array.Empty<Role>();
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Authentication required" });
                return;
            }

            var raw = user.FindFirstValue(ClaimType);
            if (string.IsNullOrEmpty(raw) || !System.Enum.TryParse<Role>(raw, ignoreCase: true, out var role) || !_allowed.Contains(role))
            {
                context.Result = new ObjectResult(new { error = $"Requires one of: {string.Join(", ", _allowed)}" })
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };
            }
        }
    }
}
