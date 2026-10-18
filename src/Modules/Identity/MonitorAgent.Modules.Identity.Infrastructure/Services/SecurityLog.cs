using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Application;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Identity.Infrastructure.Services;

internal sealed class SecurityLog(IdentityDb db, IHttpContextAccessor http, TimeProvider clock) : ISecurityLog, ISecurityEventQueries
{
    public async Task RecordAsync(Guid? userId, SecurityEventType type, CancellationToken ct)
    {
        var ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        db.SecurityEvents.Add(SecurityEvent.Record(userId, type, clock.GetUtcNow(), ip));
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<SecurityEventDto>> ListAsync(Guid? userId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.SecurityEvents.AsNoTracking();
        if (userId is not null) query = query.Where(e => e.UserId == userId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new SecurityEventDto(e.Id, e.UserId, e.Type, e.OccurredAt, e.IpAddress))
            .ToListAsync(ct);
        return new PagedResult<SecurityEventDto>(items, page, pageSize, total);
    }
}

internal sealed class IdentityLinks(IdentityModuleOptions options) : IIdentityLinks
{
    public string ConfirmEmail(Guid userId, string token) =>
        $"{options.PortalUrl.TrimEnd('/')}/auth/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}";

    public string ResetPassword(string email, string token) =>
        $"{options.PortalUrl.TrimEnd('/')}/auth/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
}
