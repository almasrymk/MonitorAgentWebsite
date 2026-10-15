using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Customers.Application;

/// <summary>The person's membership in a company, and invitations sent to their email.</summary>
internal sealed class CompanyPersonalData(ICustomerAccounts accounts, IUserDirectory users) : IPersonalDataSource
{
    public string Section => "company";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken ct)
    {
        var account = await accounts.ForMemberAsync(userId, ct);
        var member = account?.MemberFor(userId);
        var email = (await users.FindAsync(userId, ct))?.Email;
        var invitations = email is null ? [] : (await accounts.WithInvitationsToAsync(email, ct))
            .SelectMany(a => a.Invitations.Where(i => string.Equals(i.Email, email, StringComparison.OrdinalIgnoreCase))
                .Select(i => new { company = a.Name, role = i.Role.ToString(), status = i.Status.ToString(), i.CreatedAt }))
            .ToList();
        if (member is null && invitations.Count == 0) return null;
        return new
        {
            membership = member is null ? null : new { company = account!.Name, role = member.Role.ToString(), member.JoinedAt },
            invitationsReceived = invitations,
        };
    }

    public async Task<Result> CanEraseAsync(Guid userId, CancellationToken ct) =>
        await accounts.ForMemberAsync(userId, ct) is { } account ? account.CanForget(userId) : Result.Success();

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        var email = (await users.FindAsync(userId, ct))?.Email ?? "";
        var touched = (await accounts.WithInvitationsToAsync(email, ct)).ToList();
        if (await accounts.ForMemberAsync(userId, ct) is { } own && touched.All(a => a.Id != own.Id)) touched.Add(own);
        foreach (var account in touched)
        {
            var forgotten = account.Forget(userId, email);
            if (forgotten.IsFailure) throw new InvalidOperationException($"Erasure was checked first but failed: {forgotten.Error!.Code}");
        }
        await accounts.SaveAsync(ct);
    }
}
