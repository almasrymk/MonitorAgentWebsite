using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MonitorAgent.Modules.Identity.Application;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Modules.Identity.Infrastructure.Services;

internal sealed class SignInSession(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, IPasswordHasher<ApplicationUser> hasher)
    : ISignInSession
{
    // Hashing a throwaway password for unknown emails keeps the response time the same as for real accounts.
    private static string? _dummyHash;

    public async Task<(SignInStatus Status, Guid? UserId)> PasswordAsync(string email, string password, bool remember)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            _dummyHash ??= hasher.HashPassword(new ApplicationUser(), Guid.NewGuid().ToString());
            hasher.VerifyHashedPassword(new ApplicationUser(), _dummyHash, password);
            return (SignInStatus.Failed, null);
        }

        // SignInManager reports "not confirmed" before it checks the password, which would tell anyone that the
        // email has an account. Check the password first so only the owner learns the account needs confirming.
        if (!user.EmailConfirmed)
        {
            if (await users.IsLockedOutAsync(user)) return (SignInStatus.LockedOut, user.Id);
            if (await users.CheckPasswordAsync(user, password)) return (SignInStatus.NotConfirmed, user.Id);
            await users.AccessFailedAsync(user);
            return (await users.IsLockedOutAsync(user) ? SignInStatus.LockedOut : SignInStatus.Failed, user.Id);
        }

        var result = await signIn.PasswordSignInAsync(user, password, remember, lockoutOnFailure: true);
        return (ToStatus(result), user.Id);
    }

    public async Task<(SignInStatus Status, Guid? UserId)> SecondFactorAsync(string code, bool isRecoveryCode, bool remember)
    {
        if (await signIn.GetTwoFactorAuthenticationUserAsync() is not { } user) return (SignInStatus.Failed, null);
        var result = isRecoveryCode
            ? await signIn.TwoFactorRecoveryCodeSignInAsync(code)
            : await signIn.TwoFactorAuthenticatorSignInAsync(code, remember, rememberClient: false);
        return (ToStatus(result), user.Id);
    }

    public async Task RefreshAsync(Guid userId) => await signIn.RefreshSignInAsync(await GetAsync(userId));

    public async Task MarkMfaAsync(Guid userId) =>
        await signIn.SignInWithClaimsAsync(await GetAsync(userId), isPersistent: false, [new Claim("amr", "mfa")]);

    public Task SignOutAsync() => signIn.SignOutAsync();

    private async Task<ApplicationUser> GetAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("User not found.");

    private static SignInStatus ToStatus(Microsoft.AspNetCore.Identity.SignInResult result) => result switch
    {
        { Succeeded: true } => SignInStatus.Succeeded,
        { RequiresTwoFactor: true } => SignInStatus.RequiresMfa,
        { IsLockedOut: true } => SignInStatus.LockedOut,
        { IsNotAllowed: true } => SignInStatus.NotConfirmed,
        _ => SignInStatus.Failed,
    };
}
