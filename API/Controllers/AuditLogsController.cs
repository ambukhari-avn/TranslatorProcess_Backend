using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TranslationBackend.API.Filters;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.API.Controllers
{
    /// <summary>Read-only view of the audit trail, for SuperUsers.</summary>
    [ApiController]
    [Authorize]
    [RequireRole(Role.SuperUser)]
    [Route("api/audit-logs")]
    public class AuditLogsController : ControllerBase
    {
        private readonly IAuditService _audit;

        public AuditLogsController(IAuditService audit)
        {
            _audit = audit;
        }

        /// <summary>GET /api/audit-logs?page=&amp;pageSize=&amp;search=&amp;sortBy=&amp;sortOrder=&amp;from=&amp;to= (newest first by default)</summary>
        [HttpGet]
        public async Task<IActionResult> GetPaged([FromQuery] AuditLogFilterDto filter, CancellationToken ct)
            => Ok(await _audit.GetPagedAsync(filter, ct));
    }
}
