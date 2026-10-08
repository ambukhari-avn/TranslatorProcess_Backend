using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Model;
using TranslationBackend.Infrastructure.Data;
using TranslationBackend.Utils;

namespace TranslationBackend.Application.Services
{
    public class AuditService : IAuditService
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<AuditService> _logger;

        public AuditService(AppDbContext context, IHttpContextAccessor http, ILogger<AuditService> logger)
        {
            _context = context;
            _http = http;
            _logger = logger;
        }

        public async Task LogAsync(string action, string? userId = null, string? userName = null,
            string? entityType = null, string? entityId = null, CancellationToken ct = default)
        {
            try
            {
                // Callers need not pass the user: fall back to the request's claims, then to a lookup.
                if (string.IsNullOrEmpty(userId))
                    userId = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userName))
                    userName = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userId))
                    userName = await _context.Users.AsNoTracking().Where(u => u.Id == userId)
                        .Select(u => u.UserName ?? u.Name).FirstOrDefaultAsync(ct);

                _context.AuditLogs.Add(new AuditLog
                {
                    Action = Truncate(action, 500)!,
                    UserId = userId,
                    UserName = Truncate(userName, 256),
                    EntityType = Truncate(entityType, 64),
                    EntityId = Truncate(entityId, 64),
                    Timestamp = DateTimeHelper.Now,
                });
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // An audit failure must never break the operation it describes.
                _logger.LogError(ex, "Failed to write audit row: {Action}", action);
            }
        }

        public async Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditLogFilterDto filter, CancellationToken ct = default)
        {
            var query = _context.AuditLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim();
                query = query.Where(x => x.Action.Contains(s)
                                         || (x.UserName != null && x.UserName.Contains(s))
                                         || (x.EntityType != null && x.EntityType.Contains(s)));
            }
            if (filter.From.HasValue) query = query.Where(x => x.Timestamp >= filter.From.Value);
            if (filter.To.HasValue) query = query.Where(x => x.Timestamp <= filter.To.Value);

            var sortBy = (filter.SortBy ?? "timestamp").Trim().ToLowerInvariant();
            var desc = !string.Equals(filter.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);
            query = sortBy switch
            {
                "id" => desc ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id),
                "action" => desc ? query.OrderByDescending(x => x.Action) : query.OrderBy(x => x.Action),
                "username" => desc ? query.OrderByDescending(x => x.UserName) : query.OrderBy(x => x.UserName),
                _ => desc ? query.OrderByDescending(x => x.Timestamp) : query.OrderBy(x => x.Timestamp),
            };

            var total = await query.CountAsync(ct);
            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 200);
            var items = await query.Skip((page - 1) * size).Take(size)
                .Select(x => new AuditLogDto
                {
                    Id = x.Id, Action = x.Action, UserId = x.UserId, UserName = x.UserName,
                    EntityType = x.EntityType, EntityId = x.EntityId, Timestamp = x.Timestamp,
                })
                .ToListAsync(ct);

            return new PagedResult<AuditLogDto> { Items = items, Page = page, PageSize = size, TotalCount = total };
        }

        private static string? Truncate(string? value, int max)
            => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
    }
}
