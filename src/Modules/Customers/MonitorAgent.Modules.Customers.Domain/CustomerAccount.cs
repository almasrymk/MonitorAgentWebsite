using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Customers.Domain;

/// <summary>What a member may do in the company account. Only owners manage the team; the rest apply in later phases.</summary>
public enum MemberRole { Owner, Billing, Technical, Viewer }

public enum InvitationStatus { Pending, Accepted, Revoked }

public sealed class Member : Entity
{
    private Member() { }

    internal Member(Guid userId, MemberRole role, DateTimeOffset joinedAt)
    {
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid UserId { get; private set; }
    public MemberRole Role { get; internal set; }
    public DateTimeOffset JoinedAt { get; private set; }
}

public sealed class Invitation : Entity
{
    private Invitation() { }

    internal Invitation(string email, MemberRole role, string tokenHash, Guid invitedBy, DateTimeOffset at, DateTimeOffset expiresAt)
    {
        Email = email;
        Role = role;
        TokenHash = tokenHash;
        InvitedBy = invitedBy;
        CreatedAt = at;
        ExpiresAt = expiresAt;
    }

    public string Email { get; private set; } = "";
    public MemberRole Role { get; private set; }
    /// <summary>SHA-256 of the emailed token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; } = "";
    public Guid InvitedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public InvitationStatus Status { get; internal set; }

    public bool IsOpen(DateTimeOffset now) => Status == InvitationStatus.Pending && now < ExpiresAt;
}

public sealed record CompanyCreated(Guid CompanyId, Guid OwnerUserId) : DomainEvent;
public sealed record MemberJoined(Guid CompanyId, Guid UserId, MemberRole Role) : DomainEvent;
public sealed record MemberRemoved(Guid CompanyId, Guid UserId) : DomainEvent;

/// <summary>
/// A customer company: the tenant. Everything a customer owns (licenses, devices, invoices) hangs off this id, and
/// a signed-in user only ever reaches the one company they are a member of.
/// </summary>
public sealed class CustomerAccount : AggregateRoot
{
    public const int MaxMembers = 50;
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    private readonly List<Member> _members = [];
    private readonly List<Invitation> _invitations = [];

    private CustomerAccount() { }

    public string Name { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<Member> Members => _members;
    public IReadOnlyList<Invitation> Invitations => _invitations;

    public static CustomerAccount Create(string name, Guid ownerUserId, DateTimeOffset at)
    {
        var account = new CustomerAccount { Name = Guard.NotEmpty(name, "Company name", 150), CreatedAt = at };
        account._members.Add(new Member(ownerUserId, MemberRole.Owner, at));
        account.Raise(new CompanyCreated(account.Id, ownerUserId));
        return account;
    }

    public Member? MemberFor(Guid userId) => _members.SingleOrDefault(m => m.UserId == userId);

    public Result Rename(Guid by, string name)
    {
        if (!IsOwner(by)) return CustomersErrors.NotOwner;
        Name = Guard.NotEmpty(name, "Company name", 150);
        return Result.Success();
    }

    /// <param name="existingMemberEmails">Emails of current members, so the same person isn't invited twice.</param>
    public Result<Invitation> Invite(Guid by, string email, MemberRole role, string tokenHash, DateTimeOffset at, IReadOnlyCollection<string> existingMemberEmails)
    {
        if (!IsOwner(by)) return CustomersErrors.NotOwner;
        email = Guard.NotEmpty(email, "Email", 256).ToLowerInvariant();
        if (existingMemberEmails.Contains(email, StringComparer.OrdinalIgnoreCase)) return CustomersErrors.AlreadyMember;
        if (_invitations.Any(i => i.IsOpen(at) && i.Email == email)) return CustomersErrors.AlreadyInvited;
        if (_members.Count + _invitations.Count(i => i.IsOpen(at)) >= MaxMembers) return CustomersErrors.TeamFull;

        var invitation = new Invitation(email, role, tokenHash, by, at, at + InvitationLifetime);
        _invitations.Add(invitation);
        return invitation;
    }

    public Result RevokeInvitation(Guid by, Guid invitationId)
    {
        if (!IsOwner(by)) return CustomersErrors.NotOwner;
        var invitation = _invitations.SingleOrDefault(i => i.Id == invitationId && i.Status == InvitationStatus.Pending);
        if (invitation is null) return CustomersErrors.InvitationNotFound;
        invitation.Status = InvitationStatus.Revoked;
        return Result.Success();
    }

    /// <summary>The invited person joins. The caller must have found this account by the token's hash.</summary>
    public Result Accept(string tokenHash, Guid userId, string userEmail, DateTimeOffset at)
    {
        var invitation = _invitations.SingleOrDefault(i => i.TokenHash == tokenHash);
        if (invitation is null || !invitation.IsOpen(at)) return CustomersErrors.InvalidInvitation;
        if (!string.Equals(invitation.Email, userEmail, StringComparison.OrdinalIgnoreCase)) return CustomersErrors.InvitationForAnotherEmail;
        if (MemberFor(userId) is not null) return CustomersErrors.AlreadyMember;

        invitation.Status = InvitationStatus.Accepted;
        _members.Add(new Member(userId, invitation.Role, at));
        Raise(new MemberJoined(Id, userId, invitation.Role));
        return Result.Success();
    }

    public Result ChangeRole(Guid by, Guid memberUserId, MemberRole role)
    {
        if (!IsOwner(by)) return CustomersErrors.NotOwner;
        var member = MemberFor(memberUserId);
        if (member is null) return CustomersErrors.MemberNotFound;
        if (member.Role == MemberRole.Owner && role != MemberRole.Owner && OwnerCount == 1) return CustomersErrors.LastOwner;
        member.Role = role;
        return Result.Success();
    }

    /// <summary>An owner removes someone, or any member leaves (removing themselves).</summary>
    public Result Remove(Guid by, Guid memberUserId)
    {
        if (by != memberUserId && !IsOwner(by)) return CustomersErrors.NotOwner;
        var member = MemberFor(memberUserId);
        if (member is null) return CustomersErrors.MemberNotFound;
        if (member.Role == MemberRole.Owner && OwnerCount == 1) return CustomersErrors.LastOwner;
        _members.Remove(member);
        Raise(new MemberRemoved(Id, memberUserId));
        return Result.Success();
    }

    private int OwnerCount => _members.Count(m => m.Role == MemberRole.Owner);
    private bool IsOwner(Guid userId) => MemberFor(userId)?.Role == MemberRole.Owner;
}
