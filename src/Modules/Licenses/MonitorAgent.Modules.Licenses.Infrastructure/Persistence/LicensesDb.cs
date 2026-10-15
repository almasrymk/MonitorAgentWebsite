using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MonitorAgent.Modules.Licenses.Application;
using MonitorAgent.Modules.Licenses.Domain;

namespace MonitorAgent.Modules.Licenses.Infrastructure.Persistence;

/// <summary>The Licenses module's own schema: which LicensingPlatform customer each company is, and each order's license.</summary>
public sealed class LicensesDb(DbContextOptions<LicensesDb> options) : DbContext(options)
{
    public const string Schema = "licenses";

    public DbSet<PlatformCustomerLink> Links => Set<PlatformCustomerLink>();
    public DbSet<LicenseGrant> Grants => Set<LicenseGrant>();

    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcConverter =
        new(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtcConverter =
        new(v => v.HasValue ? v.Value.UtcDateTime : null, v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<PlatformCustomerLink>(l =>
        {
            l.ToTable("CustomerLinks");
            l.HasKey(x => x.CompanyId);
            l.Property(x => x.LinkedAt).HasConversion(UtcConverter);
            l.HasIndex(x => x.PlatformCustomerId).IsUnique();
        });

        modelBuilder.Entity<LicenseGrant>(g =>
        {
            g.ToTable("Grants");
            g.HasKey(x => x.Id);
            g.Property(x => x.Id).ValueGeneratedNever();
            g.Ignore(x => x.DomainEvents);
            g.Property(x => x.OrderReference).HasMaxLength(100).IsRequired();
            g.HasIndex(x => x.OrderReference).IsUnique();
            g.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            g.Property(x => x.LicenseNumber).HasMaxLength(64);
            g.Property(x => x.ProtectedProductKey).HasMaxLength(2000);
            g.Property(x => x.CreatedAt).HasConversion(UtcConverter);
            g.Property(x => x.IssuedAt).HasConversion(NullableUtcConverter);
            g.Property(x => x.ExpiresAt).HasConversion(NullableUtcConverter);
            g.HasIndex(x => new { x.CompanyId, x.PlatformLicenseId });
        });
    }
}

internal sealed class LicenseStore(LicensesDb db) : ILicenseStore
{
    public Task<PlatformCustomerLink?> LinkForAsync(Guid companyId, CancellationToken ct) =>
        db.Links.SingleOrDefaultAsync(l => l.CompanyId == companyId, ct);

    public void Add(PlatformCustomerLink link) => db.Links.Add(link);

    public Task<LicenseGrant?> GrantForOrderAsync(string orderReference, CancellationToken ct) =>
        db.Grants.SingleOrDefaultAsync(g => g.OrderReference == orderReference, ct);

    public Task<LicenseGrant?> GrantForLicenseAsync(Guid companyId, Guid platformLicenseId, CancellationToken ct) =>
        db.Grants.SingleOrDefaultAsync(g => g.CompanyId == companyId && g.PlatformLicenseId == platformLicenseId, ct);

    public async Task<IReadOnlyList<LicenseGrant>> ExpiringAsync(DateTimeOffset now, DateTimeOffset until, int take, CancellationToken ct) =>
        await db.Grants.Where(g => g.Status == GrantStatus.Issued && g.ExpiresAt > now && g.ExpiresAt <= until)
            .OrderBy(g => g.ExpiresAt).Take(take).ToListAsync(ct);

    public Task<bool> RenewedAsync(LicenseGrant grant, CancellationToken ct) =>
        db.Grants.AnyAsync(g => g.Id != grant.Id && g.CompanyId == grant.CompanyId && g.PlanId == grant.PlanId
            && g.Status == GrantStatus.Issued && g.ExpiresAt > grant.ExpiresAt, ct);

    public void Add(LicenseGrant grant) => db.Grants.Add(grant);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class DataProtectionKeyProtector(IDataProtectionProvider provider) : IKeyProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("MonitorAgent.Licenses.ProductKey.v1");

    public string Protect(string productKey) => _protector.Protect(productKey);
    public string Unprotect(string protectedKey) => _protector.Unprotect(protectedKey);
}
