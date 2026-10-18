using Microsoft.EntityFrameworkCore;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Identity.Infrastructure.Services;

/// <summary>The person's security log: sign-ins, password and two-step changes, and the addresses they came from.</summary>
internal sealed class SecurityEventsPersonalData(IdentityDb db) : IPersonalDataSource
{
    public string Section => "securityEvents";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken ct) =>
        await db.SecurityEvents.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new { type = e.Type.ToString(), e.OccurredAt, e.IpAddress })
            .ToListAsync(ct);

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        foreach (var e in await db.SecurityEvents.Where(e => e.UserId == userId && e.IpAddress != null).ToListAsync(ct)) e.ForgetIpAddress();
        await db.SaveChangesAsync(ct);
    }
}
