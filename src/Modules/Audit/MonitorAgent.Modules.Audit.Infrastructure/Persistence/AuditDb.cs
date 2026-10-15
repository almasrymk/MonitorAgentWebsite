using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Audit.Application;

namespace MonitorAgent.Modules.Audit.Infrastructure.Persistence;

public sealed class AuditDb(DbContextOptions<AuditDb> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    private static readonly ValueConverter<DateTimeOffset, DateTime> Utc =
        new(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("Entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.At).HasConversion(Utc);
            e.Property(x => x.Action).HasMaxLength(AuditEntry.MaxAction);
            e.Property(x => x.Target).HasMaxLength(AuditEntry.MaxTarget);
            e.Property(x => x.Detail).HasMaxLength(AuditEntry.MaxDetail);
            e.Property(x => x.IpAddress).HasMaxLength(45);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.Action, x.At });
            e.HasIndex(x => new { x.ActorUserId, x.At });
            e.HasIndex(x => new { x.Target, x.At });
        });
    }
}

/// <summary>Writes audit lines in their own small transaction, separate from the change they describe.</summary>
internal sealed class AuditTrail(AuditDb db, IHttpContextAccessor http, TimeProvider clock) : IAuditTrail
{
    public async Task RecordAsync(AuditRecord record, CancellationToken ct)
    {
        db.Entries.Add(AuditEntry.From(record, clock.GetUtcNow(), http.HttpContext?.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class AuditStore(AuditDb db) : IAuditStore
{
    public async Task<PagedResult<AuditEntry>> ListAsync(string? action, Guid? actor, string? target, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Entries.AsNoTracking();
        if (!string.IsNullOrEmpty(action)) query = query.Where(e => e.Action.StartsWith(action));
        if (actor is not null) query = query.Where(e => e.ActorUserId == actor);
        if (!string.IsNullOrEmpty(target)) query = query.Where(e => e.Target == target);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.At).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditEntry>(items, page, pageSize, total);
    }
}
