namespace MonitorAgent.Modules.Identity.Contracts;

public sealed record UserSummary(Guid Id, string Email, string FullName);

/// <summary>Read-only lookups other modules may use to show who an account belongs to.</summary>
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
