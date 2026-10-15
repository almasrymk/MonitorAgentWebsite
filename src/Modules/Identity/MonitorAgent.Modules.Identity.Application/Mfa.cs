using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Application;

public sealed record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

/// <summary>The key to add to an authenticator app, as text and as an otpauth:// URI for a QR code.</summary>
public sealed record GetMfaSetupQuery : IQuery<Result<AuthenticatorSetup>>;

/// <summary>Turns on two-step verification after the user proves a code from the app. Returns one-time recovery codes.</summary>
public sealed record EnableMfaCommand(string Code) : ICommand<Result<RecoveryCodesDto>>;

/// <summary>Turns off two-step verification. Needs the password; staff accounts can't turn it off.</summary>
public sealed record DisableMfaCommand(string Password) : ICommand<Result>;

public sealed record RegenerateRecoveryCodesCommand : ICommand<Result<RecoveryCodesDto>>;

internal sealed class EnableMfaValidator : AbstractValidator<EnableMfaCommand>
{
    public EnableMfaValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
}

internal sealed class DisableMfaValidator : AbstractValidator<DisableMfaCommand>
{
    public DisableMfaValidator() => RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
}

internal sealed class GetMfaSetupHandler(IUserAccounts users, ISignInSession session, ICurrentUser me) : IQueryHandler<GetMfaSetupQuery, Result<AuthenticatorSetup>>
{
    public async Task<Result<AuthenticatorSetup>> HandleAsync(GetMfaSetupQuery query, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user) return IdentityErrors.Unauthenticated;
        if (user.MfaEnabled) return IdentityErrors.MfaAlreadyEnabled;
        var setup = await users.GetAuthenticatorSetupAsync(userId);
        // Creating the key rotates the security stamp; reissue this session so only the others are affected.
        await session.RefreshAsync(userId);
        return setup;
    }
}

internal sealed class EnableMfaHandler(IUserAccounts users, ISignInSession session, ICurrentUser me, ISecurityLog log)
    : ICommandHandler<EnableMfaCommand, Result<RecoveryCodesDto>>
{
    public async Task<Result<RecoveryCodesDto>> HandleAsync(EnableMfaCommand command, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user) return IdentityErrors.Unauthenticated;
        if (user.MfaEnabled) return IdentityErrors.MfaAlreadyEnabled;
        var code = command.Code.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (!await users.VerifyAuthenticatorCodeAsync(userId, code)) return IdentityErrors.InvalidMfaCode;

        await users.SetMfaEnabledAsync(userId, true);
        var codes = await users.NewRecoveryCodesAsync(userId);
        await session.MarkMfaAsync(userId);
        await log.RecordAsync(userId, SecurityEventType.MfaEnabled, ct);
        return new RecoveryCodesDto(codes);
    }
}

internal sealed class DisableMfaHandler(IUserAccounts users, ISignInSession session, ICurrentUser me, ISecurityLog log)
    : ICommandHandler<DisableMfaCommand, Result>
{
    public async Task<Result> HandleAsync(DisableMfaCommand command, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user) return IdentityErrors.Unauthenticated;
        if (user.Roles.Any(IdentityRoles.Staff.Contains)) return IdentityErrors.MfaRequiredForStaff;
        if (!user.MfaEnabled) return IdentityErrors.MfaNotEnabled;
        if (!await users.CheckPasswordAsync(userId, command.Password)) return IdentityErrors.WrongPassword;

        await users.SetMfaEnabledAsync(userId, false);
        await session.RefreshAsync(userId);
        await log.RecordAsync(userId, SecurityEventType.MfaDisabled, ct);
        return Result.Success();
    }
}

internal sealed class RegenerateRecoveryCodesHandler(IUserAccounts users, ICurrentUser me, ISecurityLog log)
    : ICommandHandler<RegenerateRecoveryCodesCommand, Result<RecoveryCodesDto>>
{
    public async Task<Result<RecoveryCodesDto>> HandleAsync(RegenerateRecoveryCodesCommand command, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user) return IdentityErrors.Unauthenticated;
        if (!user.MfaEnabled) return IdentityErrors.MfaNotEnabled;
        var codes = await users.NewRecoveryCodesAsync(userId);
        await log.RecordAsync(userId, SecurityEventType.RecoveryCodesRegenerated, ct);
        return new RecoveryCodesDto(codes);
    }
}
