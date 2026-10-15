using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MonitorAgent.Modules.Identity.Domain;

namespace MonitorAgent.Modules.Identity.Infrastructure.Persistence;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) => Id = Guid.CreateVersion7();
}

/// <summary>
/// The Identity module's own database schema: users, roles, the security log, and the data-protection keys that
/// sign cookies and account tokens (kept here so sessions survive restarts and work across instances).
/// </summary>
public sealed class IdentityDb(DbContextOptions<IdentityDb> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";

    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Stored as UTC datetime2 so times sort and filter in SQL on every provider (SQLite can't order DateTimeOffset).
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcConverter =
        new(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.FullName).HasMaxLength(100).IsRequired();
            user.Property(u => u.Email).HasMaxLength(256).IsRequired();
            user.Property(u => u.CreatedAt).HasConversion(UtcConverter);
        });

        builder.Entity<SecurityEvent>(e =>
        {
            e.ToTable("SecurityEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.IpAddress).HasMaxLength(45);
            e.Property(x => x.OccurredAt).HasConversion(UtcConverter);
            e.HasIndex(x => new { x.UserId, x.OccurredAt });
            e.HasIndex(x => x.OccurredAt);
        });
    }
}
