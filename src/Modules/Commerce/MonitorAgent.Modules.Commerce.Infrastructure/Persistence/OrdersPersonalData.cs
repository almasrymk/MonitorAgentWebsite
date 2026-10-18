using Microsoft.EntityFrameworkCore;
using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Modules.Commerce.Infrastructure.Persistence;

/// <summary>
/// Orders the person placed. Nothing is erased: orders, invoices and payments are tax records the company must keep, and
/// they name the company, not the person (only the account id, which leads nowhere once the account is deleted).
/// </summary>
internal sealed class OrdersPersonalData(CommerceDb db) : IPersonalDataSource
{
    public string Section => "orders";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.PlacedBy == userId)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new { o.Number, o.ProductName, o.PlanName, o.Total, o.Currency, status = o.Status.ToString(), o.PlacedAt, o.PaidAt, o.PaidVia })
            .ToListAsync(ct);
        return orders.Count == 0 ? null : orders;
    }
}
