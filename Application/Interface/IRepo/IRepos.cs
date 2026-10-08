using TranslationBackend.Application.DTOs;
using TranslationBackend.Domain.Model;

namespace TranslationBackend.Application.Interface.IRepo
{
    public interface IEmployeeRepo
    {
        Task<(IReadOnlyList<Users> Items, int TotalCount)> GetPagedAsync(EmployeeFilterDto filter, CancellationToken ct = default);
        Task<Users?> GetByIdAsync(string id, CancellationToken ct = default);
        Task<Users?> SetActiveAsync(string id, bool isActive, CancellationToken ct = default);
    }

    public interface IJobRepo
    {
        Task AddAsync(TranslationJob job, CancellationToken ct = default);
        Task<TranslationJob?> GetAsync(Guid id, CancellationToken ct = default);
        Task SaveAsync(CancellationToken ct = default);

        /// <summary>One user's jobs, newest first.</summary>
        Task<(IReadOnlyList<TranslationJob> Items, int TotalCount)> ListByUserAsync(string userId, JobFilterDto filter, CancellationToken ct = default);

        /// <summary>Everyone's jobs with their users, newest first.</summary>
        Task<(IReadOnlyList<TranslationJob> Items, int TotalCount)> ListAllAsync(JobFilterDto filter, CancellationToken ct = default);

        Task<IReadOnlyList<Guid>> ActiveIdsAsync(CancellationToken ct = default);
        Task<int> CountActiveForUserAsync(string userId, CancellationToken ct = default);
        Task<IReadOnlyList<TranslationJob>> FinishedBeforeAsync(DateTime cutoff, CancellationToken ct = default);
    }
}
