using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TranslationBackend.API.Filters;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.API.Controllers
{
    /// <summary>Who converted what: every user's translations. SuperUser (the "Developer") only.</summary>
    [ApiController]
    [Authorize]
    [RequireRole(Role.SuperUser)]
    [Route("api/history")]
    public class HistoryController : ControllerBase
    {
        private readonly IJobService _jobs;

        public HistoryController(IJobService jobs)
        {
            _jobs = jobs;
        }

        /// <summary>GET /api/history?page=&amp;pageSize=&amp;status=&amp;search=&amp;userId=&amp;from=&amp;to=</summary>
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] JobFilterDto filter, CancellationToken ct)
            => Ok(await _jobs.HistoryAsync(filter, ct));
    }
}
