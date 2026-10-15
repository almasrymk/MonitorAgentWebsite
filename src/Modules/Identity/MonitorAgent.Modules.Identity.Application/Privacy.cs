using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Application;

/// <summary>Everything the website holds about the signed-in person, one section per module.</summary>
public sealed record PersonalDataExport(DateTimeOffset ExportedAt, PersonalAccountData Account, IReadOnlyDictionary<string, object> Sections);

public sealed record PersonalAccountData(Guid Id, string Email, string FullName, DateTimeOffset CreatedAt, bool EmailConfirmed,
    bool TwoStepVerification, IReadOnlyList<string> Roles);

public sealed record ExportMyDataQuery : IQuery<Result<PersonalDataExport>>;

/// <summary>
/// Deletes the signed-in customer's account: every module removes or anonymizes what it holds, then the account itself
/// loses its name, email and password and is signed out everywhere. The password is asked again because this can't be undone.
/// </summary>
public sealed record DeleteMyAccountCommand(string Password) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "account.delete";
    public string AuditTarget => "own account";
}

internal sealed class DeleteMyAccountValidator : AbstractValidator<DeleteMyAccountCommand>
{
    public DeleteMyAccountValidator() => RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
}

internal sealed class ExportMyDataHandler(IUserAccounts users, ICurrentUser me, IEnumerable<IPersonalDataSource> sources, TimeProvider clock)
    : IQueryHandler<ExportMyDataQuery, Result<PersonalDataExport>>
{
    public async Task<Result<PersonalDataExport>> HandleAsync(ExportMyDataQuery query, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user) return IdentityErrors.Unauthenticated;
        var sections = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (await source.ExportAsync(userId, ct) is { } data) sections[source.Section] = data;
        }
        var account = new PersonalAccountData(user.Id, user.Email, user.FullName, user.CreatedAt, user.EmailConfirmed, user.MfaEnabled, user.Roles);
        return new PersonalDataExport(clock.GetUtcNow(), account, sections);
    }
}

internal sealed class DeleteMyAccountHandler(IUserAccounts users, ISignInSession session, ICurrentUser me, IEnumerable<IPersonalDataSource> sources,
    IEmailSender email, ISecurityLog log) : ICommandHandler<DeleteMyAccountCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteMyAccountCommand command, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user) return IdentityErrors.Unauthenticated;
        // Staff accounts are closed by an admin when someone leaves, so the records they signed stay attributable.
        if (user.Roles.Any(IdentityRoles.Staff.Contains)) return IdentityErrors.StaffAccount;
        if (!await users.CheckPasswordAsync(userId, command.Password)) return IdentityErrors.WrongPassword;

        var all = sources.ToList();
        foreach (var source in all)
        {
            var allowed = await source.CanEraseAsync(userId, ct);
            if (allowed.IsFailure) return allowed;
        }
        foreach (var source in all) await source.EraseAsync(userId, ct);

        var address = user.Email;
        await users.AnonymizeAsync(userId);
        await session.SignOutAsync();
        await log.RecordAsync(userId, SecurityEventType.AccountDeleted, ct);
        await email.SendAsync(Emails.AccountDeleted(address), ct);
        return Result.Success();
    }
}
