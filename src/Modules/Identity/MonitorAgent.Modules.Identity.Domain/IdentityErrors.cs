using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Domain;

/// <summary>Error codes the Identity endpoints return. They are part of the API contract.</summary>
public static class IdentityErrors
{
    // Deliberately one error for an unknown email and a wrong password, so sign-in can't be used to find accounts.
    public static readonly Error InvalidCredentials = Error.Unauthorized("INVALID_CREDENTIALS", "Email or password is incorrect.");
    public static readonly Error EmailNotConfirmed = Error.Forbidden("EMAIL_NOT_CONFIRMED", "Confirm your email address before signing in.");
    public static readonly Error LockedOut = new("ACCOUNT_LOCKED", "Too many failed attempts. Try again later.", ErrorKind.Locked);
    public static readonly Error InvalidMfaCode = Error.Unauthorized("INVALID_MFA_CODE", "The verification code is incorrect.");
    public static readonly Error MfaSessionExpired = Error.Unauthorized("MFA_SESSION_EXPIRED", "Sign in again to continue.");
    public static readonly Error InvalidToken = Error.Validation("INVALID_TOKEN", "This link is invalid or has expired.");
    public static readonly Error MfaAlreadyEnabled = Error.Conflict("MFA_ALREADY_ENABLED", "Two-step verification is already on.");
    public static readonly Error MfaNotEnabled = Error.Conflict("MFA_NOT_ENABLED", "Two-step verification is not on.");
    public static readonly Error WrongPassword = Error.Validation("WRONG_PASSWORD", "The current password is incorrect.");
    public static readonly Error MfaRequiredForStaff = Error.Forbidden("MFA_REQUIRED_FOR_STAFF", "Staff accounts must keep two-step verification on.");
    public static readonly Error StaffAccount = Error.Forbidden("STAFF_ACCOUNT", "Staff accounts are closed by an administrator.");
    public static readonly Error Unauthenticated = Error.Unauthorized("UNAUTHENTICATED", "Sign in to continue.");

    public static Error PasswordRejected(string reasons) => Error.Validation("PASSWORD_REJECTED", reasons);
}
