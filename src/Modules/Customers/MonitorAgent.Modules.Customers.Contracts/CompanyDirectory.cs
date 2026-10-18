namespace MonitorAgent.Modules.Customers.Contracts;

/// <summary>The caller's place in their company. <see cref="Role"/> is one of Owner, Billing, Technical, Viewer.</summary>
public sealed record CompanyMembership(Guid CompanyId, string CompanyName, string Role)
{
    public bool IsOwner => Role == "Owner";
    public bool CanManageDevices => Role is "Owner" or "Technical";
}

public sealed record CompanySummary(Guid Id, string Name, Guid OwnerUserId);

/// <summary>Read-only company lookups for other modules. This is how they resolve the tenant of a signed-in user.</summary>
public interface ICompanyDirectory
{
    Task<CompanyMembership?> MembershipOfAsync(Guid userId, CancellationToken ct);
    Task<CompanySummary?> FindAsync(Guid companyId, CancellationToken ct);
}
