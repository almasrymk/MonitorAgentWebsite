using Microsoft.EntityFrameworkCore;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Customers.Domain;

namespace MonitorAgent.Modules.Customers.Infrastructure.Persistence;

internal sealed class CompanyDirectory(CustomersDb db) : ICompanyDirectory
{
    public async Task<CompanyMembership?> MembershipOfAsync(Guid userId, CancellationToken ct) =>
        await db.Accounts.AsNoTracking()
            .SelectMany(a => a.Members.Where(m => m.UserId == userId), (a, m) => new { a.Id, a.Name, m.Role })
            .Select(x => new CompanyMembership(x.Id, x.Name, x.Role.ToString()))
            .SingleOrDefaultAsync(ct);

    public async Task<CompanySummary?> FindAsync(Guid companyId, CancellationToken ct) =>
        await db.Accounts.AsNoTracking().Where(a => a.Id == companyId)
            .Select(a => new CompanySummary(a.Id, a.Name,
                a.Members.Where(m => m.Role == MemberRole.Owner).OrderBy(m => m.JoinedAt).Select(m => m.UserId).First()))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, CompanySummary>> FindManyAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken ct)
    {
        if (companyIds.Count == 0) return new Dictionary<Guid, CompanySummary>();
        return await db.Accounts.AsNoTracking().Where(a => companyIds.Contains(a.Id))
            .Select(a => new CompanySummary(a.Id, a.Name,
                a.Members.Where(m => m.Role == MemberRole.Owner).OrderBy(m => m.JoinedAt).Select(m => m.UserId).First()))
            .ToDictionaryAsync(c => c.Id, ct);
    }
}
