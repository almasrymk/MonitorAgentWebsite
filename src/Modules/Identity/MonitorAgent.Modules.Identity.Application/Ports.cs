using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Application;

public sealed record UserAccount(Guid Id, string Email, string FullName, bool EmailConfirmed, bool MfaEnabled, IReadOnlyList<string> Roles);

public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

public enum SignInStatus { Succeeded, RequiresMfa, NotConfirmed, LockedOut, Failed }

/// <summary>User storage and the account operations around it (ASP.NET Core Identity in Infrastructure).</summary>
internal interface IUserAccounts
{
    Task<UserAccount?> FindByEmailAsync(string email);
    Task<UserAccount?> FindByIdAsync(Guid id);
    Task<Result<UserAccount>> CreateAsync(string email, string fullName, string password, string role);
    Task<string> CreateEmailConfirmationTokenAsync(Guid userId);
    Task<bool> ConfirmEmailAsync(Guid userId, string token);
    Task<string> CreatePasswordResetTokenAsync(Guid userId);
    Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword);
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<bool> CheckPasswordAsync(Guid userId, string password);
    Task<AuthenticatorSetup> GetAuthenticatorSetupAsync(Guid userId);
    Task<bool> VerifyAuthenticatorCodeAsync(Guid userId, string code);
    Task SetMfaEnabledAsync(Guid userId, bool enabled);
    Task<IReadOnlyList<string>> NewRecoveryCodesAsync(Guid userId);
    Task RevokeSessionsAsync(Guid userId);
}

/// <summary>The caller's sign-in session (the auth cookie).</summary>
internal interface ISignInSession
{
    Task<(SignInStatus Status, Guid? UserId)> PasswordAsync(string email, string password, bool remember);
    Task<(SignInStatus Status, Guid? UserId)> SecondFactorAsync(string code, bool isRecoveryCode, bool remember);
    /// <summary>Re-issues this session after a change that rotated the security stamp, keeping how it was authenticated.</summary>
    Task RefreshAsync(Guid userId);
    /// <summary>Re-issues this session as second-factor authenticated, right after the user proved a fresh authenticator code.</summary>
    Task MarkMfaAsync(Guid userId);
    Task SignOutAsync();
}

internal interface ISecurityLog
{
    Task RecordAsync(Guid? userId, SecurityEventType type, CancellationToken ct);
}

/// <summary>Builds the links that go into account emails.</summary>
public sealed record SecurityEventDto(Guid Id, Guid? UserId, SecurityEventType Type, DateTimeOffset OccurredAt, string? IpAddress);

internal interface ISecurityEventQueries
{
    Task<PagedResult<SecurityEventDto>> ListAsync(Guid? userId, int page, int pageSize, CancellationToken ct);
}

internal interface IIdentityLinks
{
    string ConfirmEmail(Guid userId, string token);
    string ResetPassword(string email, string token);
}
