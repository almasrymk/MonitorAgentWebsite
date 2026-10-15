using MonitorAgent.Modules.Customers.Domain;

namespace MonitorAgent.Modules.Customers.Application;

/// <summary>
/// Loads company accounts. There is deliberately no "by id" lookup: a handler reaches a company only through the
/// signed-in user's membership or an invitation token, so one customer can never address another's data.
/// </summary>
internal interface ICustomerAccounts
{
    Task<CustomerAccount?> ForMemberAsync(Guid userId, CancellationToken ct);
    Task<CustomerAccount?> ByInvitationTokenAsync(string tokenHash, CancellationToken ct);
    /// <summary>Companies holding an invitation to this email, for deleting that person's data only.</summary>
    Task<IReadOnlyList<CustomerAccount>> WithInvitationsToAsync(string email, CancellationToken ct);
    void Add(CustomerAccount account);
    Task SaveAsync(CancellationToken ct);
}
