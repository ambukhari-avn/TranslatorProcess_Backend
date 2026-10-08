using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TranslationBackend.API.Filters;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.API.Controllers
{
    /// <summary>
    /// Employee directory and the sign-in approval workflow, for SuperUsers (the Developers) only.
    /// </summary>
    [ApiController]
    [Authorize]
    [RequireRole(Role.SuperUser)]
    [Route("api/employees")]
    public class EmployeesController : ControllerBase
    {
        private readonly IEmployeeService _service;
        private readonly IAuditService _audit;
        private readonly IRealtimeNotifier _notifier;
        private readonly ILogger<EmployeesController> _logger;

        public EmployeesController(IEmployeeService service, IAuditService audit, IRealtimeNotifier notifier, ILogger<EmployeesController> logger)
        {
            _service = service;
            _audit = audit;
            _notifier = notifier;
            _logger = logger;
        }

        private string? CallerId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
        private string? CallerName() => User.FindFirstValue(ClaimTypes.Name);

        /// <summary>Reload every open employee screen and show the admins a toast.</summary>
        private async Task NotifyAdminsAsync(string action, string title, string message, string employeeId)
        {
            await _notifier.EmployeeChangedAsync(action, employeeId);
            await _notifier.NotifyAdminsAsync(new RealtimeNotificationDto
            {
                Type = $"Employee{char.ToUpper(action[0]) + action[1..]}",
                Title = title,
                Message = message,
                EntityType = "Employee",
                EntityId = employeeId,
            });
        }

        /// <summary>GET /api/employees?page=&amp;pageSize=&amp;search=&amp;isActive=&amp;role=&amp;approvalStatus=</summary>
        [HttpGet]
        public async Task<IActionResult> GetPaged([FromQuery] EmployeeFilterDto filter, CancellationToken ct)
        {
            try
            {
                return Ok(await _service.GetPagedAsync(filter, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in GET /api/employees");
                return StatusCode(500, new { error = "An error occurred while retrieving employees" });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            var employee = await _service.GetByIdAsync(id, ct);
            return employee is null ? NotFound(new { error = $"Employee {id} not found" }) : Ok(employee);
        }

        /// <summary>PATCH /api/employees/{id}/status: activate or deactivate an employee (never yourself).</summary>
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> SetActive(string id, [FromBody] EmployeeStatusDto dto, CancellationToken ct)
        {
            if (string.Equals(CallerId(), id, StringComparison.Ordinal) && !dto.IsActive)
                return BadRequest(new { error = "You cannot deactivate your own account." });

            var current = await _service.GetByIdAsync(id, ct);
            if (current is null) return NotFound(new { error = $"Employee {id} not found" });

            var updated = await _service.SetActiveAsync(id, dto.IsActive, ct);
            if (updated is null) return NotFound(new { error = $"Employee {id} not found" });

            var verb = dto.IsActive ? "Activated" : "Deactivated";
            await _audit.LogAsync($"{verb} employee {updated.FullName} ({updated.Email ?? updated.UserName})",
                CallerId(), CallerName(), "Employee", updated.Id, ct);
            await NotifyAdminsAsync(dto.IsActive ? "activated" : "deactivated",
                dto.IsActive ? "Employee reactivated" : "Employee deactivated",
                $"{CallerName() ?? "An admin"} {(dto.IsActive ? "reactivated" : "deactivated")} {updated.FullName}.", updated.Id);
            return Ok(updated);
        }
    }
}
