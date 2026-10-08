using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Infrastructure.Data;

namespace TranslationBackend.API.Controllers
{
    /// <summary>Anonymous health check for monitoring and IIS: is the database up, and does the translation service answer?</summary>
    [ApiController]
    [Route("api/health")]
    public class HealthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IExtractionClient _extraction;

        public HealthController(AppDbContext db, IExtractionClient extraction)
        {
            _db = db;
            _extraction = extraction;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var database = await _db.Database.CanConnectAsync(ct);
            var extraction = await _extraction.IsHealthyAsync(ct);
            var body = new { status = database && extraction ? "ok" : "degraded", database, extraction };
            return database ? Ok(body) : StatusCode(503, body);
        }
    }
}
