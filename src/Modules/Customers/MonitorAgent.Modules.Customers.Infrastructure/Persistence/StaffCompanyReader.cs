using Microsoft.EntityFrameworkCore;
using MonitorAgent.Modules.Customers.Application;
using MonitorAgent.Modules.Customers.Domain;

namespace MonitorAgent.Modules.Customers.Infrastructure.Persistence;

internal sealed class StaffCompanyReader(CustomersDb db) : IStaffCompanyReader
{
    public async Task<(IReadOnlyList<CustomerAccount> Items, int Total)> SearchAsync(string? name, Guid? memberUserId, int page, int pageSize,
        CancellationToken ct)
    {
        var query = db.Accounts.AsNoTracking();
        if (name is not null) query = query.Where(a => a.Name.Contains(name));
        if (memberUserId is { } userId) query = query.Where(a => a.Members.Any(m => m.UserId == userId));
        var total = await query.CountAsync(ct);
        var items = await query.Include(a => a.Members).OrderByDescending(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<CustomerAccount?> FindAsync(Guid companyId, CancellationToken ct) =>
        db.Accounts.AsNoTracking().Include(a => a.Members).Include(a => a.Invitations).SingleOrDefaultAsync(a => a.Id == companyId, ct);
}
