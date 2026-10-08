using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TranslationBackend.API.Filters;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.API.Controllers
{
    public class UploadDocumentRequest
    {
        [Required] public IFormFile File { get; set; } = null!;
        /// <summary>"ru" (default) or "kk".</summary>
        public string Language { get; set; } = "ru";
        /// <summary>Second-model verification and repair pass (default on).</summary>
        public bool Verify { get; set; } = true;
    }

    /// <summary>
    /// A user's translations: upload, follow progress, cancel, download. A user sees only their own; a SuperUser can open
    /// anyone's. Errors are <c>{ "code", "message" }</c>.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/documents")]
    public class DocumentsController : ControllerBase
    {
        private readonly IJobService _jobs;

        public DocumentsController(IJobService jobs)
        {
            _jobs = jobs;
        }

        private JobCaller Caller() => new(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            CanSeeAll: string.Equals(User.FindFirstValue(RequireRoleAttribute.ClaimType), nameof(Role.SuperUser), StringComparison.Ordinal));

        private IActionResult NotFoundJob(Guid id) => NotFound(new { code = "JOB_NOT_FOUND", message = $"No translation with id {id}." });

        /// <summary>POST /api/documents (multipart): starts a translation and returns it as Queued.</summary>
        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload([FromForm] UploadDocumentRequest request, CancellationToken ct)
        {
            await using var stream = request.File.OpenReadStream();
            var job = await _jobs.CreateAsync(Caller(), request.File.FileName, request.File.Length, stream, request.Language, request.Verify, ct);
            return CreatedAtAction(nameof(Get), new { id = job.Id }, job);
        }

        /// <summary>GET /api/documents?page=&amp;pageSize=&amp;status=&amp;search=: your translations, newest first.</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] JobFilterDto filter, CancellationToken ct)
            => Ok(await _jobs.ListMineAsync(Caller().UserId, filter, ct));

        /// <summary>GET /api/documents/{id}: status, progress, stage and counts. Poll this (or listen to the JobChanged event).</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var job = await _jobs.GetAsync(Caller(), id, ct);
            return job is null ? NotFoundJob(id) : Ok(job);
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        {
            var job = await _jobs.CancelAsync(Caller(), id, ct);
            return job is null ? NotFoundJob(id) : Ok(job);
        }

        /// <summary>GET /api/documents/{id}/download: the translated Word file.</summary>
        [HttpGet("{id:guid}/download")]
        public Task<IActionResult> Download(Guid id, CancellationToken ct) => SendFile(id, JobFileKind.Output, ct);

        /// <summary>GET /api/documents/{id}/review: the review document with the rows to check.</summary>
        [HttpGet("{id:guid}/review")]
        public Task<IActionResult> Review(Guid id, CancellationToken ct) => SendFile(id, JobFileKind.Review, ct);

        private async Task<IActionResult> SendFile(Guid id, JobFileKind kind, CancellationToken ct)
        {
            var file = await _jobs.GetFileAsync(Caller(), id, kind, ct);
            return file is null ? NotFoundJob(id) : PhysicalFile(file.FullPath, file.ContentType, file.DownloadName);
        }
    }
}
