using Microsoft.AspNetCore.Identity;
using TranslationBackend.Domain.Enum;

namespace TranslationBackend.Domain.Model
{
    /// <summary>
    /// Application user. Extends <see cref="IdentityUser"/> for its key and user-name plumbing; authentication is done
    /// by Microsoft, so the inherited <c>PasswordHash</c> column is never populated.
    /// </summary>
    public class Users : IdentityUser
    {
        public string Name { get; set; } = null!;

        public Role Role { get; set; } = Role.Employee;

        /// <summary>Switched off by a Developer to cut someone off; new accounts start active.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public ICollection<TranslationJob> Jobs { get; set; } = new List<TranslationJob>();
    }
}
