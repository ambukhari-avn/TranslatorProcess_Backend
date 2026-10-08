using Microsoft.AspNetCore.Identity;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Utils;

namespace TranslationBackend.Infrastructure.Data;

/// <summary>One account to seed, read from configuration (Seed:Users).</summary>
public class SeedUser
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Role Role { get; set; } = Role.SuperUser;
}

/// <summary>
/// Creates the privileged accounts listed in configuration (Seed:Users) so they exist in every environment:
/// SuperUser = the Developers. There is no password: Microsoft
/// authenticates them, and on first sign-in the account is matched by email. Existing accounts are promoted.
/// Add a person to the list and restart to give them access.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IEnumerable<SeedUser> seeds, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<Users>>();
        foreach (var seed in seeds.Where(s => !string.IsNullOrWhiteSpace(s.Email)))
            await SeedOneAsync(userManager, seed, logger);
    }

    private static async Task SeedOneAsync(UserManager<Users> userManager, SeedUser seed, ILogger logger)
    {
        var email = seed.Email.Trim();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            if (existing.Role == seed.Role && existing.IsActive)
                return;
            existing.Role = seed.Role;
            existing.IsActive = true;
            existing.UpdatedAt = DateTimeHelper.Now;
            var update = await userManager.UpdateAsync(existing);
            if (!update.Succeeded)
                throw new InvalidOperationException($"Could not promote '{email}' to {seed.Role}: {string.Join(", ", update.Errors.Select(e => e.Description))}");
            logger.LogInformation("Promoted {Email} to {Role}", email, seed.Role);
            return;
        }

        // Same username convention as the Microsoft sign-in: <local part>_ms.
        var username = $"{email.Split('@')[0]}_ms";
        if (await userManager.FindByNameAsync(username) != null)
            username = $"{username}_{Guid.NewGuid().ToString()[..4]}";

        var user = new Users
        {
            Name = string.IsNullOrWhiteSpace(seed.Name) ? email : seed.Name.Trim(),
            UserName = username,
            Email = email,
            Role = seed.Role,
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = DateTimeHelper.Now,
            UpdatedAt = DateTimeHelper.Now,
        };
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not seed {seed.Role} '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
        logger.LogInformation("Seeded {Role} {Email}", seed.Role, email);
    }
}
