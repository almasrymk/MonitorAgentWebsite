using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Customers.Domain;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Customers.Application;

public sealed record MemberDto(Guid UserId, string Email, string FullName, MemberRole Role, DateTimeOffset JoinedAt);
public sealed record InvitationDto(Guid Id, string Email, MemberRole Role, DateTimeOffset ExpiresAt);

/// <summary>The caller's company. Pending invitations are only listed for owners.</summary>
public sealed record CompanyDto(Guid Id, string Name, MemberRole MyRole, IReadOnlyList<MemberDto> Members, IReadOnlyList<InvitationDto> Invitations);

public sealed record CreateCompanyCommand(string Name) : ICommand<Result<CompanyDto>>;
public sealed record GetMyCompanyQuery : IQuery<Result<CompanyDto>>;
public sealed record RenameCompanyCommand(string Name) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "company.rename";
    public string AuditTarget => "company";
    public string? AuditDetail => Name;
}

internal sealed class CompanyNameValidator : AbstractValidator<CreateCompanyCommand>
{
    public CompanyNameValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
}

internal sealed class RenameCompanyValidator : AbstractValidator<RenameCompanyCommand>
{
    public RenameCompanyValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
}

internal sealed class CompanyView(IUserDirectory users, TimeProvider clock)
{
    public async Task<CompanyDto> ForAsync(CustomerAccount account, Guid viewer, CancellationToken ct)
    {
        var people = await users.FindManyAsync(account.Members.Select(m => m.UserId).ToList(), ct);
        var members = account.Members
            .OrderBy(m => m.JoinedAt)
            .Select(m => people.TryGetValue(m.UserId, out var u)
                ? new MemberDto(m.UserId, u.Email, u.FullName, m.Role, m.JoinedAt)
                : new MemberDto(m.UserId, "", "", m.Role, m.JoinedAt))
            .ToList();
        var myRole = account.MemberFor(viewer)!.Role;
        var now = clock.GetUtcNow();
        var invitations = myRole == MemberRole.Owner
            ? account.Invitations.Where(i => i.IsOpen(now)).OrderBy(i => i.CreatedAt)
                .Select(i => new InvitationDto(i.Id, i.Email, i.Role, i.ExpiresAt)).ToList()
            : [];
        return new CompanyDto(account.Id, account.Name, myRole, members, invitations);
    }
}

internal sealed class CreateCompanyHandler(ICustomerAccounts accounts, ICurrentUser me, CompanyView view, TimeProvider clock)
    : ICommandHandler<CreateCompanyCommand, Result<CompanyDto>>
{
    public async Task<Result<CompanyDto>> HandleAsync(CreateCompanyCommand command, CancellationToken ct)
    {
        var userId = me.UserId!.Value;
        if (await accounts.ForMemberAsync(userId, ct) is not null) return CustomersErrors.AlreadyInCompany;
        var account = CustomerAccount.Create(command.Name, userId, clock.GetUtcNow());
        accounts.Add(account);
        await accounts.SaveAsync(ct);
        return await view.ForAsync(account, userId, ct);
    }
}

internal sealed class GetMyCompanyHandler(ICustomerAccounts accounts, ICurrentUser me, CompanyView view)
    : IQueryHandler<GetMyCompanyQuery, Result<CompanyDto>>
{
    public async Task<Result<CompanyDto>> HandleAsync(GetMyCompanyQuery query, CancellationToken ct)
    {
        var userId = me.UserId!.Value;
        if (await accounts.ForMemberAsync(userId, ct) is not { } account) return CustomersErrors.NoCompany;
        return await view.ForAsync(account, userId, ct);
    }
}

internal sealed class RenameCompanyHandler(ICustomerAccounts accounts, ICurrentUser me) : ICommandHandler<RenameCompanyCommand, Result>
{
    public async Task<Result> HandleAsync(RenameCompanyCommand command, CancellationToken ct)
    {
        var userId = me.UserId!.Value;
        if (await accounts.ForMemberAsync(userId, ct) is not { } account) return CustomersErrors.NoCompany;
        var result = account.Rename(userId, command.Name);
        if (result.IsSuccess) await accounts.SaveAsync(ct);
        return result;
    }
}
