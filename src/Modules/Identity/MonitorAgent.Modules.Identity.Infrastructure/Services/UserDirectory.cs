using Microsoft.EntityFrameworkCore;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Identity.Infrastructure.Services;

internal sealed class UserDirectory(IdentityDb db) : IUserDirectory
{
    public async Task<UserSummary?> FindAsync(Guid userId, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new UserSummary(u.Id, u.Email!, u.FullName)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.FullName)).ToDictionaryAsync(u => u.Id, ct);

    public async Task<UserSummary?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized)
            .Select(u => new UserSummary(u.Id, u.Email!, u.FullName)).FirstOrDefaultAsync(ct);
    }
}
