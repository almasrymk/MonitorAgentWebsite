using Microsoft.EntityFrameworkCore;
using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Modules.Audit.Infrastructure.Persistence;

/// <summary>
/// Sensitive steps the person took (revealing a product key, changing their team). The entries stay after the account is
/// deleted because the audit trail must be complete, but the address they came from is dropped.
/// </summary>
internal sealed class AuditPersonalData(AuditDb db) : IPersonalDataSource
{
    public string Section => "activity";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken ct)
    {
        var entries = await db.Entries.AsNoTracking()
            .Where(e => e.ActorUserId == userId)
            .OrderByDescending(e => e.At)
            .Select(e => new { e.At, e.Action, e.Target, e.Detail, e.IpAddress })
            .ToListAsync(ct);
        return entries.Count == 0 ? null : entries;
    }

    public Task EraseAsync(Guid userId, CancellationToken ct) =>
        db.Entries.Where(e => e.ActorUserId == userId && e.IpAddress != null).ExecuteUpdateAsync(s => s.SetProperty(e => e.IpAddress, (string?)null), ct);
}
