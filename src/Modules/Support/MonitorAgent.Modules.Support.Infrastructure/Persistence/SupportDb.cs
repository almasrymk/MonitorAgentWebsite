using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Support.Application;
using MonitorAgent.Modules.Support.Domain;

namespace MonitorAgent.Modules.Support.Infrastructure.Persistence;

public sealed class SupportDb(DbContextOptions<SupportDb> options) : DbContext(options)
{
    public const string Schema = "support";

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();

    private static readonly ValueConverter<DateTimeOffset, DateTime> Utc =
        new(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtc =
        new(v => v.HasValue ? v.Value.UtcDateTime : null, v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Ticket>(t =>
        {
            t.ToTable("Tickets");
            t.HasKey(x => x.Id);
            t.Property(x => x.Id).ValueGeneratedNever();
            t.Ignore(x => x.DomainEvents);
            t.Property(x => x.Number).HasMaxLength(20);
            t.HasIndex(x => x.Number).IsUnique();
            t.Property(x => x.Subject).HasMaxLength(Ticket.MaxSubject);
            t.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            t.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            t.Property(x => x.OpenedAt).HasConversion(Utc);
            t.Property(x => x.LastActivityAt).HasConversion(Utc);
            t.HasIndex(x => new { x.CompanyId, x.Status });
            t.HasIndex(x => new { x.Status, x.LastActivityAt });
            t.OwnsMany(x => x.Messages, m =>
            {
                m.ToTable("TicketMessages");
                m.WithOwner().HasForeignKey("TicketId");
                m.HasKey(x => x.Id);
                m.Property(x => x.Id).ValueGeneratedNever();
                m.Property(x => x.Body).HasMaxLength(Ticket.MaxBody);
                m.Property(x => x.PostedAt).HasConversion(Utc);
            });
            t.Navigation(x => x.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<Suggestion>(s =>
        {
            s.ToTable("Suggestions");
            s.HasKey(x => x.Id);
            s.Property(x => x.Id).ValueGeneratedNever();
            s.Ignore(x => x.DomainEvents);
            s.Property(x => x.Title).HasMaxLength(Suggestion.MaxTitle);
            s.Property(x => x.Body).HasMaxLength(Ticket.MaxBody);
            s.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            s.Property(x => x.StaffNote).HasMaxLength(Suggestion.MaxNote);
            s.Property(x => x.CreatedAt).HasConversion(Utc);
            s.Property(x => x.AnsweredAt).HasConversion(NullableUtc);
            s.HasIndex(x => x.CompanyId);
            s.HasIndex(x => new { x.Status, x.CreatedAt });
        });
    }
}

internal sealed class SupportStore(SupportDb db) : ISupportStore
{
    public Task<Ticket?> TicketAsync(Guid id, CancellationToken ct) => db.Tickets.SingleOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<Ticket>> CompanyTicketsAsync(Guid companyId, CancellationToken ct) =>
        await db.Tickets.Where(t => t.CompanyId == companyId).OrderByDescending(t => t.LastActivityAt).Take(200).ToListAsync(ct);

    public Task<int> OpenTicketCountAsync(Guid companyId, CancellationToken ct) =>
        db.Tickets.CountAsync(t => t.CompanyId == companyId && t.Status != TicketStatus.Closed, ct);

    /// <summary>Oldest activity first for open tickets, so the customer waiting longest is at the top.</summary>
    public async Task<PagedResult<Ticket>> TicketsAsync(TicketStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Tickets.Where(t => status == null || t.Status == status);
        var ordered = status == TicketStatus.Open ? query.OrderBy(t => t.LastActivityAt) : query.OrderByDescending(t => t.LastActivityAt);
        return new PagedResult<Ticket>(await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), page, pageSize, await query.CountAsync(ct));
    }

    public Task<Suggestion?> SuggestionAsync(Guid id, CancellationToken ct) => db.Suggestions.SingleOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Suggestion>> CompanySuggestionsAsync(Guid companyId, CancellationToken ct) =>
        await db.Suggestions.Where(s => s.CompanyId == companyId).OrderByDescending(s => s.CreatedAt).Take(200).ToListAsync(ct);

    public async Task<PagedResult<Suggestion>> SuggestionsAsync(SuggestionStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Suggestions.Where(s => status == null || s.Status == status);
        return new PagedResult<Suggestion>(await query.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct),
            page, pageSize, await query.CountAsync(ct));
    }

    public void Add(Ticket ticket) => db.Tickets.Add(ticket);

    public void Add(Suggestion suggestion) => db.Suggestions.Add(suggestion);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
