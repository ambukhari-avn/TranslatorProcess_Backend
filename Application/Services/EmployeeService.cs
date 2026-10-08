using Microsoft.AspNetCore.Identity;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IRepo;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Utils;

namespace TranslationBackend.Application.Services
{
    public class EmployeeService : IEmployeeService
    {
        private readonly IEmployeeRepo _repo;
        private readonly UserManager<Users> _userManager;
        private readonly ILogger<EmployeeService> _logger;

        public EmployeeService(IEmployeeRepo repo, UserManager<Users> userManager, ILogger<EmployeeService> logger)
        {
            _repo = repo;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<PagedResult<EmployeeDto>> GetPagedAsync(EmployeeFilterDto filter, CancellationToken ct = default)
        {
            var (items, total) = await _repo.GetPagedAsync(filter, ct);
            return new PagedResult<EmployeeDto>
            {
                Items = items.Select(ToDto).ToList(),
                Page = Math.Max(1, filter.Page),
                PageSize = Math.Clamp(filter.PageSize, 1, 200),
                TotalCount = total,
            };
        }

        public async Task<EmployeeDto?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var entity = await _repo.GetByIdAsync(id, ct);
            return entity is null ? null : ToDto(entity);
        }

        public async Task<EmployeeDto?> SetActiveAsync(string id, bool isActive, CancellationToken ct = default)
        {
            var updated = await _repo.SetActiveAsync(id, isActive, ct);
            return updated is null ? null : ToDto(updated);
        }

        private static EmployeeDto ToDto(Users x) => new()
        {
            Id = x.Id,
            UserName = x.UserName ?? string.Empty,
            Email = x.Email,
            FullName = x.Name,
            Role = x.Role.ToString(),
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
        };
    }
}
