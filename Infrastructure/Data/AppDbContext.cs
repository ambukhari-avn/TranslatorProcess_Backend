using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TranslationBackend.Domain.Model;

namespace TranslationBackend.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<Users>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TranslationJob> TranslationJobs => Set<TranslationJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enums are stored by name, so a plain SELECT shows "SuperUser" / "Done" instead of 2 / 3.
        modelBuilder.Entity<Users>().Property(x => x.Role).HasConversion<string>().HasMaxLength(32);

        // Audit history must survive deleting a user: restrict-delete, and the user name is copied into the row.
        modelBuilder.Entity<AuditLog>()
            .HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AuditLog>().HasIndex(x => x.Timestamp).IsDescending();
        modelBuilder.Entity<AuditLog>().HasIndex(x => x.UserId);
        modelBuilder.Entity<AuditLog>().HasIndex(x => new { x.EntityType, x.EntityId });

        var job = modelBuilder.Entity<TranslationJob>();
        job.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        job.HasOne(x => x.User).WithMany(u => u.Jobs).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        job.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true);
        job.HasIndex(x => x.Status);
        job.HasIndex(x => x.CreatedAt).IsDescending();
        job.Ignore(x => x.IsFinished);
    }
}
