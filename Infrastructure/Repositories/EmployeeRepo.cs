using Microsoft.EntityFrameworkCore;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IRepo;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Infrastructure.Data;
using TranslationBackend.Utils;

namespace TranslationBackend.Infrastructure.Repositories
{
    public class EmployeeRepo : IEmployeeRepo
    {
        private readonly AppDbContext _context;

        public EmployeeRepo(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(IReadOnlyList<Users> Items, int TotalCount)> GetPagedAsync(EmployeeFilterDto filter, CancellationToken ct = default)
        {
            var query = _context.Users.AsNoTracking().AsQueryable();

            if (filter.IsActive.HasValue) query = query.Where(x => x.IsActive == filter.IsActive.Value);
            if (filter.Role.HasValue) query = query.Where(x => x.Role == filter.Role.Value);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim();
                query = query.Where(x =>
                    (x.UserName != null && x.UserName.Contains(s)) ||
                    (x.Email != null && x.Email.Contains(s)) ||
                    x.Name.Contains(s));
            }

            var totalCount = await query.CountAsync(ct);
            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 200);

            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.UserName)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync(ct);

            return (items, totalCount);
        }

        public Task<Users?> GetByIdAsync(string id, CancellationToken ct = default)
            => _context.Users.FirstOrDefaultAsync(x => x.Id == id, ct);

        public async Task<Users?> SetActiveAsync(string id, bool isActive, CancellationToken ct = default)
        {
            var employee = await _context.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (employee is null) return null;

            employee.IsActive = isActive;
            employee.UpdatedAt = DateTimeHelper.Now;
            await _context.SaveChangesAsync(ct);
            return employee;
        }
    }
}
