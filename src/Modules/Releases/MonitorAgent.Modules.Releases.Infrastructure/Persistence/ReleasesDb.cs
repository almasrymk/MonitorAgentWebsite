using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MonitorAgent.Modules.Releases.Application;
using MonitorAgent.Modules.Releases.Domain;

namespace MonitorAgent.Modules.Releases.Infrastructure.Persistence;

public sealed class ReleasesDb(DbContextOptions<ReleasesDb> options) : DbContext(options)
{
    public const string Schema = "releases";

    public DbSet<Release> Releases => Set<Release>();

    private static readonly ValueConverter<DateTimeOffset, DateTime> Utc =
        new(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtc =
        new(v => v.HasValue ? v.Value.UtcDateTime : null, v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Release>(r =>
        {
            r.ToTable("Releases");
            r.HasKey(x => x.Id);
            r.Property(x => x.Id).ValueGeneratedNever();
            r.Ignore(x => x.DomainEvents);
            r.Ignore(x => x.Version);
            r.Property(x => x.Channel).HasMaxLength(20);
            r.Property(x => x.Notes).HasMaxLength(4000);
            r.Property(x => x.Manifest).HasMaxLength(ManifestReader.MaxBytes);
            r.Property(x => x.Signature).HasMaxLength(512);
            r.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            r.Property(x => x.CreatedAt).HasConversion(Utc);
            r.Property(x => x.PublishedAt).HasConversion(NullableUtc);
            r.HasIndex(x => new { x.Channel, x.Major, x.Minor, x.Patch }).IsUnique();
            r.OwnsMany(x => x.Packages, p =>
            {
                p.ToTable("ReleasePackages");
                p.WithOwner().HasForeignKey("ReleaseId");
                p.Property<int>("Id");
                p.HasKey("Id");
                p.Property(x => x.Platform).HasMaxLength(30);
                p.Property(x => x.Url).HasMaxLength(500);
                p.Property(x => x.Sha256).HasMaxLength(64);
            });
            r.Navigation(x => x.Packages).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

internal sealed class ReleaseStore(ReleasesDb db) : IReleaseStore
{
    public Task<Release?> FindAsync(Guid id, CancellationToken ct) => db.Releases.SingleOrDefaultAsync(r => r.Id == id, ct);

    public Task<bool> ExistsAsync(string channel, int major, int minor, int patch, CancellationToken ct) =>
        db.Releases.AnyAsync(r => r.Channel == channel && r.Major == major && r.Minor == minor && r.Patch == patch, ct);

    public Task<Release?> CurrentAsync(string channel, CancellationToken ct) =>
        db.Releases.Where(r => r.Channel == channel && r.Status == ReleaseStatus.Published)
            .OrderByDescending(r => r.Major).ThenByDescending(r => r.Minor).ThenByDescending(r => r.Patch)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Release>> ListAsync(bool publishedOnly, CancellationToken ct) =>
        await db.Releases.Where(r => !publishedOnly || r.Status == ReleaseStatus.Published)
            .OrderBy(r => r.Channel).ThenByDescending(r => r.Major).ThenByDescending(r => r.Minor).ThenByDescending(r => r.Patch)
            .Take(200).ToListAsync(ct);

    public void Add(Release release) => db.Releases.Add(release);

    public async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new MonitorAgent.SharedKernel.DomainException(ReleasesErrors.Exists);
        }
    }
}
