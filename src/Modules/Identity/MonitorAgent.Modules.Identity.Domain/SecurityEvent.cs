using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Domain;

public enum SecurityEventType
{
    Registered,
    EmailConfirmed,
    SignedIn,
    SignInFailed,
    LockedOut,
    SignedOut,
    SessionsRevoked,
    PasswordChanged,
    PasswordResetRequested,
    PasswordReset,
    MfaEnabled,
    MfaDisabled,
    RecoveryCodesRegenerated,
    RecoveryCodeUsed,
    AccountDeleted,
}

/// <summary>Append-only record of a security-relevant account action. Never holds passwords, tokens or codes.</summary>
public sealed class SecurityEvent : Entity
{
    private SecurityEvent() { }

    public Guid? UserId { get; private set; }
    public SecurityEventType Type { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? IpAddress { get; private set; }

    /// <summary>The account was deleted: the event stays, the address it came from doesn't.</summary>
    public void ForgetIpAddress() => IpAddress = null;

    public static SecurityEvent Record(Guid? userId, SecurityEventType type, DateTimeOffset at, string? ipAddress) => new()
    {
        UserId = userId,
        Type = type,
        OccurredAt = at,
        IpAddress = ipAddress is { Length: > 45 } ? ipAddress[..45] : ipAddress,
    };
}
