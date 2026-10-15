using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Application;

public sealed record SignInResponse(bool RequiresMfa);

public sealed record SignInCommand(string Email, string Password, bool RememberMe) : ICommand<Result<SignInResponse>>;

/// <summary>Second step of sign-in, after a password sign-in answered <c>requiresMfa</c>, with an authenticator code.</summary>
public sealed record SignInWithMfaCommand(string Code, bool RememberMe) : ICommand<Result>;

/// <summary>Second step of sign-in with one of the single-use recovery codes, for when the authenticator is lost.</summary>
public sealed record SignInWithRecoveryCodeCommand(string Code) : ICommand<Result>;

public sealed record SignOutCommand : ICommand<Result>;

/// <summary>Signs the account out everywhere, this session included.</summary>
public sealed record RevokeAllSessionsCommand : ICommand<Result>;

internal sealed class SignInValidator : AbstractValidator<SignInCommand>
{
    public SignInValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class SignInWithMfaValidator : AbstractValidator<SignInWithMfaCommand>
{
    public SignInWithMfaValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
}

internal sealed class SignInWithRecoveryCodeValidator : AbstractValidator<SignInWithRecoveryCodeCommand>
{
    public SignInWithRecoveryCodeValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
}

internal sealed class SignInHandler(ISignInSession session, ISecurityLog log) : ICommandHandler<SignInCommand, Result<SignInResponse>>
{
    public async Task<Result<SignInResponse>> HandleAsync(SignInCommand command, CancellationToken ct)
    {
        var (status, userId) = await session.PasswordAsync(command.Email.Trim(), command.Password, command.RememberMe);
        switch (status)
        {
            case SignInStatus.Succeeded:
                await log.RecordAsync(userId, SecurityEventType.SignedIn, ct);
                return new SignInResponse(RequiresMfa: false);
            case SignInStatus.RequiresMfa:
                return new SignInResponse(RequiresMfa: true);
            case SignInStatus.NotConfirmed:
                return IdentityErrors.EmailNotConfirmed;
            case SignInStatus.LockedOut:
                await log.RecordAsync(userId, SecurityEventType.LockedOut, ct);
                return IdentityErrors.LockedOut;
            default:
                await log.RecordAsync(userId, SecurityEventType.SignInFailed, ct);
                return IdentityErrors.InvalidCredentials;
        }
    }
}

internal sealed class SignInWithMfaHandler(ISignInSession session, ISecurityLog log) : ICommandHandler<SignInWithMfaCommand, Result>
{
    public async Task<Result> HandleAsync(SignInWithMfaCommand command, CancellationToken ct)
    {
        var code = command.Code.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        var (status, userId) = await session.AuthenticatorCodeAsync(code, command.RememberMe);
        return await SecondFactor.ResultAsync(status, userId, SecurityEventType.SignedIn, log, ct);
    }
}

internal sealed class SignInWithRecoveryCodeHandler(ISignInSession session, ISecurityLog log) : ICommandHandler<SignInWithRecoveryCodeCommand, Result>
{
    public async Task<Result> HandleAsync(SignInWithRecoveryCodeCommand command, CancellationToken ct)
    {
        // The dash is part of the stored recovery code, so only spaces are dropped.
        var (status, userId) = await session.RecoveryCodeAsync(command.Code.Replace(" ", "", StringComparison.Ordinal));
        return await SecondFactor.ResultAsync(status, userId, SecurityEventType.RecoveryCodeUsed, log, ct);
    }
}

internal static class SecondFactor
{
    public static async Task<Result> ResultAsync(SignInStatus status, Guid? userId, SecurityEventType onSuccess, ISecurityLog log, CancellationToken ct)
    {
        switch (status)
        {
            case SignInStatus.Succeeded:
                await log.RecordAsync(userId, onSuccess, ct);
                return Result.Success();
            case SignInStatus.LockedOut:
                await log.RecordAsync(userId, SecurityEventType.LockedOut, ct);
                return IdentityErrors.LockedOut;
            case SignInStatus.Failed when userId is null:
                return IdentityErrors.MfaSessionExpired;
            default:
                await log.RecordAsync(userId, SecurityEventType.SignInFailed, ct);
                return IdentityErrors.InvalidMfaCode;
        }
    }
}

internal sealed class SignOutHandler(ISignInSession session, ICurrentUser me, ISecurityLog log) : ICommandHandler<SignOutCommand, Result>
{
    public async Task<Result> HandleAsync(SignOutCommand command, CancellationToken ct)
    {
        var userId = me.UserId;
        await session.SignOutAsync();
        if (userId is not null) await log.RecordAsync(userId, SecurityEventType.SignedOut, ct);
        return Result.Success();
    }
}

internal sealed class RevokeAllSessionsHandler(IUserAccounts users, ISignInSession session, ICurrentUser me, ISecurityLog log)
    : ICommandHandler<RevokeAllSessionsCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeAllSessionsCommand command, CancellationToken ct)
    {
        if (me.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        await users.RevokeSessionsAsync(userId);
        await session.SignOutAsync();
        await log.RecordAsync(userId, SecurityEventType.SessionsRevoked, ct);
        return Result.Success();
    }
}
