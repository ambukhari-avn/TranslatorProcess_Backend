using Microsoft.EntityFrameworkCore;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IRepo;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Infrastructure.Data;

namespace TranslationBackend.Infrastructure.Repositories
{
    public class JobRepo : IJobRepo
    {
        private readonly AppDbContext _context;

        public JobRepo(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(TranslationJob job, CancellationToken ct = default)
        {
            _context.TranslationJobs.Add(job);
            await _context.SaveChangesAsync(ct);
        }

        public Task<TranslationJob?> GetAsync(Guid id, CancellationToken ct = default)
            => _context.TranslationJobs.Include(j => j.User).FirstOrDefaultAsync(j => j.Id == id, ct);

        public Task SaveAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

        public Task<(IReadOnlyList<TranslationJob> Items, int TotalCount)> ListByUserAsync(string userId, JobFilterDto filter, CancellationToken ct = default)
            => PageAsync(_context.TranslationJobs.AsNoTracking().Where(j => j.UserId == userId), filter, includeUserSearch: false, ct);

        public Task<(IReadOnlyList<TranslationJob> Items, int TotalCount)> ListAllAsync(JobFilterDto filter, CancellationToken ct = default)
        {
            var query = _context.TranslationJobs.AsNoTracking().Include(j => j.User).AsQueryable();
            if (!string.IsNullOrWhiteSpace(filter.UserId))
                query = query.Where(j => j.UserId == filter.UserId);
            return PageAsync(query, filter, includeUserSearch: true, ct);
        }

        public async Task<IReadOnlyList<Guid>> ActiveIdsAsync(CancellationToken ct = default)
            => await _context.TranslationJobs
                .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.Id)
                .ToListAsync(ct);

        public Task<int> CountActiveForUserAsync(string userId, CancellationToken ct = default)
            => _context.TranslationJobs.CountAsync(
                j => j.UserId == userId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running), ct);

        public async Task<IReadOnlyList<TranslationJob>> FinishedBeforeAsync(DateTime cutoff, CancellationToken ct = default)
            => await _context.TranslationJobs
                .Where(j => j.FilesDeletedAt == null && j.FinishedAt != null && j.FinishedAt < cutoff)
                .ToListAsync(ct);

        private static async Task<(IReadOnlyList<TranslationJob>, int)> PageAsync(
            IQueryable<TranslationJob> query, JobFilterDto filter, bool includeUserSearch, CancellationToken ct)
        {
            if (filter.Status.HasValue) query = query.Where(j => j.Status == filter.Status.Value);
            if (filter.From.HasValue) query = query.Where(j => j.CreatedAt >= filter.From.Value);
            if (filter.To.HasValue) query = query.Where(j => j.CreatedAt <= filter.To.Value);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim();
                query = includeUserSearch
                    ? query.Where(j => j.OriginalName.Contains(s)
                                       || (j.User != null && (j.User.Name.Contains(s) || (j.User.Email != null && j.User.Email.Contains(s)))))
                    : query.Where(j => j.OriginalName.Contains(s));
            }

            var total = await query.CountAsync(ct);
            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 200);
            var items = await query.OrderByDescending(j => j.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return (items, total);
        }
    }
}
