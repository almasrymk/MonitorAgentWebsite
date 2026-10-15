using Microsoft.EntityFrameworkCore;
using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Modules.Support.Infrastructure.Persistence;

/// <summary>
/// What the person wrote to support. Erasing the account keeps these: a ticket belongs to the company's support history,
/// and once the account is anonymized the messages show "Deleted user" as their author.
/// </summary>
internal sealed class SupportPersonalData(SupportDb db) : IPersonalDataSource
{
    public string Section => "support";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken ct)
    {
        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.OpenedBy == userId || t.Messages.Any(m => m.AuthorUserId == userId))
            .OrderByDescending(t => t.OpenedAt)
            .ToListAsync(ct);
        var suggestions = await db.Suggestions.AsNoTracking().Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        if (tickets.Count == 0 && suggestions.Count == 0) return null;
        return new
        {
            tickets = tickets.Select(t => new
            {
                t.Number, t.Subject, category = t.Category.ToString(), status = t.Status.ToString(), t.OpenedAt,
                yourMessages = t.Messages.Where(m => m.AuthorUserId == userId).Select(m => new { m.PostedAt, m.Body }),
            }),
            suggestions = suggestions.Select(s => new { s.Title, s.Body, status = s.Status.ToString(), s.CreatedAt, s.StaffNote }),
        };
    }
}
