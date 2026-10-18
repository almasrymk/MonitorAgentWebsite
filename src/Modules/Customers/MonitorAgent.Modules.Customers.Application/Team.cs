using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Customers.Domain;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Customers.Application;

public sealed record InviteMemberCommand(string Email, MemberRole Role) : ICommand<Result<InvitationDto>>, IAuditedCommand
{
    public string AuditAction => "team.invite";
    public string AuditTarget => $"email:{Email}";
    public string? AuditDetail => Role.ToString();
}
public sealed record RevokeInvitationCommand(Guid InvitationId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "team.revoke-invitation";
    public string AuditTarget => $"invitation:{InvitationId}";
}
public sealed record AcceptInvitationCommand(string Token) : ICommand<Result<CompanyDto>>;
public sealed record ChangeMemberRoleCommand(Guid UserId, MemberRole Role) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "team.change-role";
    public string AuditTarget => $"user:{UserId}";
    public string? AuditDetail => Role.ToString();
}

/// <summary>An owner removes a member, or a member removes themselves (leaves the company).</summary>
public sealed record RemoveMemberCommand(Guid UserId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "team.remove";
    public string AuditTarget => $"user:{UserId}";
}

internal sealed class InviteMemberValidator : AbstractValidator<InviteMemberCommand>
{
    public InviteMemberValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role).IsInEnum();
    }
}

internal sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator() => RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
}

internal sealed class ChangeMemberRoleValidator : AbstractValidator<ChangeMemberRoleCommand>
{
    public ChangeMemberRoleValidator() => RuleFor(x => x.Role).IsInEnum();
}

internal sealed class InviteMemberHandler(
    ICustomerAccounts accounts, ICurrentUser me, IUserDirectory users, IEmailSender email, PortalLinks portal, TimeProvider clock)
    : ICommandHandler<InviteMemberCommand, Result<InvitationDto>>
{
    public async Task<Result<InvitationDto>> HandleAsync(InviteMemberCommand command, CancellationToken ct)
    {
        var userId = me.UserId!.Value;
        if (await accounts.ForMemberAsync(userId, ct) is not { } account) return CustomersErrors.NoCompany;

        var people = await users.FindManyAsync(account.Members.Select(m => m.UserId).ToList(), ct);
        var token = InvitationTokens.New();
        var invited = account.Invite(userId, command.Email, command.Role, InvitationTokens.Hash(token), clock.GetUtcNow(),
            people.Values.Select(p => p.Email).ToList());
        if (invited.IsFailure) return invited.Error!;

        await accounts.SaveAsync(ct);
        var inviter = people.TryGetValue(userId, out var p) ? p.FullName : "";
        var link = portal.To($"invitations/accept?token={Uri.EscapeDataString(token)}");
        await email.SendAsync(Emails.Invitation(invited.Value.Email, inviter, account.Name, link), ct);
        return new InvitationDto(invited.Value.Id, invited.Value.Email, invited.Value.Role, invited.Value.ExpiresAt);
    }
}

internal sealed class RevokeInvitationHandler(ICustomerAccounts accounts, ICurrentUser me) : ICommandHandler<RevokeInvitationCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeInvitationCommand command, CancellationToken ct)
    {
        var userId = me.UserId!.Value;
        if (await accounts.ForMemberAsync(userId, ct) is not { } account) return CustomersErrors.NoCompany;
        var result = account.RevokeInvitation(userId, command.InvitationId);
        if (result.IsSuccess) await accounts.SaveAsync(ct);
        return result;
    }
}

internal sealed class AcceptInvitationHandler(ICustomerAccounts accounts, ICurrentUser me, IUserDirectory users, CompanyView view, TimeProvider clock)
    : ICommandHandler<AcceptInvitationCommand, Result<CompanyDto>>
{
    public async Task<Result<CompanyDto>> HandleAsync(AcceptInvitationCommand command, CancellationToken ct)
    {
        var userId = me.UserId!.Value;
        if (await accounts.ByInvitationTokenAsync(InvitationTokens.Hash(command.Token), ct) is not { } account)
            return CustomersErrors.InvalidInvitation;
        if (account.MemberFor(userId) is null && await accounts.ForMemberAsync(userId, ct) is not null)
            return CustomersErrors.AlreadyInCompany;
        if (await users.FindAsync(userId, ct) is not { } user) return CustomersErrors.InvalidInvitation;

        var result = account.Accept(InvitationTokens.Hash(command.Token), userId, user.Email, clock.GetUtcNow());
        if (result.IsFailure) return result.Error!;
        await accounts.SaveAsync(ct);
        return await view.ForAsync(account, userId, ct);
    }
}

internal sealed class ChangeMemberRoleHandler(ICustomerAccounts accounts, ICurrentUser me) : ICommandHandler<ChangeMemberRoleCommand, Result>
{
    public async Task<Result> HandleAsync(ChangeMemberRoleCommand command, CancellationToken ct)
    {
        var userId = me.UserId!.Value;
        if (await accounts.ForMemberAsync(userId, ct) is not { } account) return CustomersErrors.NoCompany;
        var result = account.ChangeRole(userId, command.UserId, command.Role);
        if (result.IsSuccess) await accounts.SaveAsync(ct);
        return result;
    }
}

internal sealed class RemoveMemberHandler(ICustomerAccounts accounts, ICurrentUser me) : ICommandHandler<RemoveMemberCommand, Result>
{
    public async Task<Result> HandleAsync(RemoveMemberCommand command, CancellationToken ct)
    {
        var userId = me.UserId!.Value;
        if (await accounts.ForMemberAsync(userId, ct) is not { } account) return CustomersErrors.NoCompany;
        var result = account.Remove(userId, command.UserId);
        if (result.IsSuccess) await accounts.SaveAsync(ct);
        return result;
    }
}
