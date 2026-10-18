using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MonitorAgent.Modules.Customers.Application;
using MonitorAgent.Modules.Customers.Domain;

namespace MonitorAgent.Modules.Customers.Infrastructure.Persistence;

/// <summary>The Customers module's own schema: companies, their members and invitations.</summary>
public sealed class CustomersDb(DbContextOptions<CustomersDb> options) : DbContext(options)
{
    public const string Schema = "customers";

    public DbSet<CustomerAccount> Accounts => Set<CustomerAccount>();

    // Stored as UTC datetime2 so times sort and filter in SQL on every provider.
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcConverter =
        new(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<CustomerAccount>(a =>
        {
            a.ToTable("Accounts");
            a.HasKey(x => x.Id);
            a.Property(x => x.Id).ValueGeneratedNever();
            a.Property(x => x.Name).HasMaxLength(150).IsRequired();
            a.Property(x => x.CreatedAt).HasConversion(UtcConverter);
            a.Ignore(x => x.DomainEvents);
            a.HasMany(x => x.Members).WithOne().HasForeignKey("AccountId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            a.HasMany(x => x.Invitations).WithOne().HasForeignKey("AccountId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            a.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
            a.Navigation(x => x.Invitations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Member>(m =>
        {
            m.ToTable("Members");
            m.HasKey(x => x.Id);
            m.Property(x => x.Id).ValueGeneratedNever();
            m.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            m.Property(x => x.JoinedAt).HasConversion(UtcConverter);
            // A person belongs to at most one company; this also backs the membership lookup on every request.
            m.HasIndex(x => x.UserId).IsUnique();
        });

        modelBuilder.Entity<Invitation>(i =>
        {
            i.ToTable("Invitations");
            i.HasKey(x => x.Id);
            i.Property(x => x.Id).ValueGeneratedNever();
            i.Property(x => x.Email).HasMaxLength(256).IsRequired();
            i.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            i.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            i.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            i.Property(x => x.CreatedAt).HasConversion(UtcConverter);
            i.Property(x => x.ExpiresAt).HasConversion(UtcConverter);
            i.HasIndex(x => x.TokenHash).IsUnique();
        });
    }
}

internal sealed class CustomerAccounts(CustomersDb db) : ICustomerAccounts
{
    private IQueryable<CustomerAccount> Full => db.Accounts.Include(a => a.Members).Include(a => a.Invitations).AsSplitQuery();

    public Task<CustomerAccount?> ForMemberAsync(Guid userId, CancellationToken ct) =>
        Full.SingleOrDefaultAsync(a => a.Members.Any(m => m.UserId == userId), ct);

    public Task<CustomerAccount?> ByInvitationTokenAsync(string tokenHash, CancellationToken ct) =>
        Full.SingleOrDefaultAsync(a => a.Invitations.Any(i => i.TokenHash == tokenHash), ct);

    public async Task<IReadOnlyList<CustomerAccount>> WithInvitationsToAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await Full.Where(a => a.Invitations.Any(i => i.Email == normalized)).ToListAsync(ct);
    }

    public void Add(CustomerAccount account) => db.Accounts.Add(account);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
