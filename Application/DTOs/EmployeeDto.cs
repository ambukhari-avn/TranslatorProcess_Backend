using System.ComponentModel.DataAnnotations;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.Application.DTOs
{
    /// <summary>An employee row in the management view (role and approval status as strings).</summary>
    public class EmployeeDto
    {
        public string Id { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EmployeeFilterDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        /// <summary>Free-text search across user name, email and full name.</summary>
        public string? Search { get; set; }
        public bool? IsActive { get; set; }
        public Role? Role { get; set; }
    }

    public class EmployeeStatusDto
    {
        [Required] public bool IsActive { get; set; }
    }

}
