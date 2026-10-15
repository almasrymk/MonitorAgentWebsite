using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MonitorAgent.Modules.Commerce.Application;
using MonitorAgent.Modules.Commerce.Domain;

namespace MonitorAgent.Modules.Commerce.Infrastructure.Persistence;

/// <summary>Takes invoice numbers without gaps. The counter row is the concurrency token, so two invoices can't share a number.</summary>
public sealed class InvoiceCounter
{
    public int Year { get; set; }
    public int Last { get; set; }
}

/// <summary>The Commerce module's own schema: orders and their payments, notifications already handled, invoices and the ledger.</summary>
public sealed class CommerceDb(DbContextOptions<CommerceDb> options) : DbContext(options)
{
    public const string Schema = "commerce";

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<ProcessedNotification> Notifications => Set<ProcessedNotification>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceCounter> InvoiceCounters => Set<InvoiceCounter>();
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();

    private static readonly ValueConverter<DateTimeOffset, DateTime> Utc =
        new(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtc =
        new(v => v.HasValue ? v.Value.UtcDateTime : null, v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Order>(o =>
        {
            o.ToTable("Orders");
            o.HasKey(x => x.Id);
            o.Property(x => x.Id).ValueGeneratedNever();
            o.Ignore(x => x.DomainEvents);
            o.Property(x => x.Number).HasMaxLength(20).IsRequired();
            o.HasIndex(x => x.Number).IsUnique();
            o.Property(x => x.ProductName).HasMaxLength(150);
            o.Property(x => x.PlanName).HasMaxLength(150);
            o.Property(x => x.Total).HasPrecision(18, 2);
            o.Property(x => x.Currency).HasMaxLength(3);
            o.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            o.Property(x => x.PaidVia).HasMaxLength(30);
            o.Property(x => x.LicenseNumber).HasMaxLength(64);
            o.Property(x => x.PlacedAt).HasConversion(Utc);
            o.Property(x => x.PaidAt).HasConversion(NullableUtc);
            o.HasIndex(x => new { x.CompanyId, x.PlacedAt });
            o.HasIndex(x => x.Status);
            o.HasMany(x => x.Payments).WithOne().HasForeignKey("OrderId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            o.Navigation(x => x.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Payment>(p =>
        {
            p.ToTable("Payments");
            p.HasKey(x => x.Id);
            p.Property(x => x.Id).ValueGeneratedNever();
            p.Property(x => x.Provider).HasMaxLength(30);
            p.Property(x => x.ProviderReference).HasMaxLength(100);
            p.Property(x => x.Amount).HasPrecision(18, 2);
            p.Property(x => x.Currency).HasMaxLength(3);
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.FailureReason).HasMaxLength(200);
            p.Property(x => x.CreatedAt).HasConversion(Utc);
            p.Property(x => x.CompletedAt).HasConversion(NullableUtc);
            p.HasIndex(x => new { x.Provider, x.ProviderReference }).IsUnique().HasFilter("[ProviderReference] IS NOT NULL");
        });

        modelBuilder.Entity<ProcessedNotification>(n =>
        {
            n.ToTable("ProcessedNotifications");
            n.HasKey(x => new { x.Provider, x.EventId });
            n.Property(x => x.Provider).HasMaxLength(30);
            n.Property(x => x.EventId).HasMaxLength(100);
            n.Property(x => x.ReceivedAt).HasConversion(Utc);
        });

        modelBuilder.Entity<Invoice>(i =>
        {
            i.ToTable("Invoices");
            i.HasKey(x => x.Id);
            i.Property(x => x.Id).ValueGeneratedNever();
            i.Property(x => x.Number).HasMaxLength(20);
            i.HasIndex(x => x.Number).IsUnique();
            i.HasIndex(x => x.OrderId).IsUnique();
            i.HasIndex(x => x.CompanyId);
            i.Property(x => x.CompanyName).HasMaxLength(150);
            i.Property(x => x.Description).HasMaxLength(300);
            i.Property(x => x.Amount).HasPrecision(18, 2);
            i.Property(x => x.Currency).HasMaxLength(3);
            i.Property(x => x.IssuedAt).HasConversion(Utc);
        });

        modelBuilder.Entity<InvoiceCounter>(c =>
        {
            c.ToTable("InvoiceCounters");
            c.HasKey(x => x.Year);
            c.Property(x => x.Year).ValueGeneratedNever();
            c.Property(x => x.Last).IsConcurrencyToken();
        });

        modelBuilder.Entity<LedgerEntry>(l =>
        {
            l.ToTable("Ledger");
            l.HasKey(x => x.Id);
            l.Property(x => x.Id).ValueGeneratedNever();
            l.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            l.Property(x => x.Amount).HasPrecision(18, 2);
            l.Property(x => x.Currency).HasMaxLength(3);
            l.Property(x => x.Provider).HasMaxLength(30);
            l.Property(x => x.Reference).HasMaxLength(100);
            l.Property(x => x.At).HasConversion(Utc);
            l.HasIndex(x => x.OrderId);
            l.HasIndex(x => x.At);
        });
    }
}

internal sealed class CommerceStore(CommerceDb db) : ICommerceStore
{
    private IQueryable<Order> Orders => db.Orders.Include(o => o.Payments);

    public Task<Order?> OrderAsync(Guid orderId, CancellationToken ct) => Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public Task<Order?> OrderForPaymentAsync(string provider, string providerReference, CancellationToken ct) =>
        Orders.SingleOrDefaultAsync(o => o.Payments.Any(p => p.Provider == provider && p.ProviderReference == providerReference), ct);

    public async Task<IReadOnlyList<Order>> OrdersOfAsync(Guid companyId, CancellationToken ct) =>
        await Orders.Where(o => o.CompanyId == companyId).OrderByDescending(o => o.PlacedAt).Take(200).ToListAsync(ct);

    public async Task<(IReadOnlyList<Order> Items, int Total)> OrdersAsync(OrderStatus? status, Guid? companyId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Orders.AsQueryable();
        if (status is not null) query = query.Where(o => o.Status == status);
        if (companyId is not null) query = query.Where(o => o.CompanyId == companyId);
        var total = await query.CountAsync(ct);
        var items = await query.Include(o => o.Payments).OrderByDescending(o => o.PlacedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<Order>> PaidUnfulfilledAsync(int max, CancellationToken ct) =>
        await Orders.Where(o => o.Status == OrderStatus.Paid).OrderBy(o => o.PaidAt).Take(max).ToListAsync(ct);

    public Task<bool> NotificationSeenAsync(string provider, string eventId, CancellationToken ct) =>
        db.Notifications.AnyAsync(n => n.Provider == provider && n.EventId == eventId, ct);

    public void Add(Order order) => db.Orders.Add(order);
    public void Add(ProcessedNotification notification) => db.Notifications.Add(notification);
    public void Add(LedgerEntry entry) => db.Ledger.Add(entry);

    public async Task<IReadOnlyList<LedgerLine>> LedgerAsync(DateTimeOffset from, DateTimeOffset until, int max, CancellationToken ct) =>
        await db.Ledger.AsNoTracking().Where(l => l.At >= from && l.At < until).OrderBy(l => l.At).Take(max)
            .Join(db.Orders, l => l.OrderId, o => o.Id, (l, o) => new LedgerLine(l, o.Number, o.ProductName, o.PlanName, o.CompanyId))
            .ToListAsync(ct);
    public void Add(Invoice invoice) => db.Invoices.Add(invoice);

    public Task<Invoice?> InvoiceForOrderAsync(Guid orderId, CancellationToken ct) => db.Invoices.SingleOrDefaultAsync(i => i.OrderId == orderId, ct);

    public async Task<IReadOnlyList<Invoice>> InvoicesOfAsync(Guid companyId, CancellationToken ct) =>
        await db.Invoices.Where(i => i.CompanyId == companyId).OrderByDescending(i => i.IssuedAt).ToListAsync(ct);

    public async Task<string> NextInvoiceNumberAsync(int year, CancellationToken ct)
    {
        var counter = db.InvoiceCounters.Local.SingleOrDefault(c => c.Year == year)
            ?? await db.InvoiceCounters.SingleOrDefaultAsync(c => c.Year == year, ct);
        if (counter is null)
        {
            counter = new InvoiceCounter { Year = year };
            db.InvoiceCounters.Add(counter);
        }
        counter.Last++;
        return $"INV-{year}-{counter.Last:D6}";
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Unique keys (notification, invoice number) and the invoice counter's concurrency token: someone else won.
            db.ChangeTracker.Clear();
            throw new SaveConflictException(ex);
        }
    }
}
