using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MonitorAgent.Modules.Identity.Application;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Infrastructure.Services;

internal sealed class UserAccounts(UserManager<ApplicationUser> users, TimeProvider clock) : IUserAccounts
{
    private const string Issuer = "Monitor Agent";

    public async Task<UserAccount?> FindByEmailAsync(string email) =>
        await users.FindByEmailAsync(email.Trim()) is { } user ? await ToAccountAsync(user) : null;

    public async Task<UserAccount?> FindByIdAsync(Guid id) =>
        await users.FindByIdAsync(id.ToString()) is { } user ? await ToAccountAsync(user) : null;

    public async Task<Result<UserAccount>> CreateAsync(string email, string fullName, string password, string role)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            FullName = fullName,
            CreatedAt = clock.GetUtcNow(),
        };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) return ToError(created);
        await users.AddToRoleAsync(user, role);
        return await ToAccountAsync(user);
    }

    public async Task<string> CreateEmailConfirmationTokenAsync(Guid userId) =>
        Encode(await users.GenerateEmailConfirmationTokenAsync(await GetAsync(userId)));

    public async Task<bool> ConfirmEmailAsync(Guid userId, string token)
    {
        if (await users.FindByIdAsync(userId.ToString()) is not { } user || Decode(token) is not { } raw) return false;
        return (await users.ConfirmEmailAsync(user, raw)).Succeeded;
    }

    public async Task<string> CreatePasswordResetTokenAsync(Guid userId) =>
        Encode(await users.GeneratePasswordResetTokenAsync(await GetAsync(userId)));

    public async Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword)
    {
        if (Decode(token) is not { } raw) return IdentityErrors.InvalidToken;
        var user = await GetAsync(userId);
        var reset = await users.ResetPasswordAsync(user, raw, newPassword);
        if (reset.Succeeded)
        {
            // A reset proves control of the mailbox, so it also clears a lockout from the attack that may have caused it.
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
            return Result.Success();
        }
        return reset.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)) ? IdentityErrors.InvalidToken : ToError(reset);
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var changed = await users.ChangePasswordAsync(await GetAsync(userId), currentPassword, newPassword);
        if (changed.Succeeded) return Result.Success();
        return changed.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)) ? IdentityErrors.WrongPassword : ToError(changed);
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password) =>
        await users.CheckPasswordAsync(await GetAsync(userId), password);

    public async Task<AuthenticatorSetup> GetAuthenticatorSetupAsync(Guid userId)
    {
        var user = await GetAsync(userId);
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }
        var uri = $"otpauth://totp/{UrlEncoder.Default.Encode(Issuer)}:{UrlEncoder.Default.Encode(user.Email!)}" +
                  $"?secret={key}&issuer={UrlEncoder.Default.Encode(Issuer)}&digits=6";
        return new AuthenticatorSetup(FormatKey(key!), uri);
    }

    public async Task<bool> VerifyAuthenticatorCodeAsync(Guid userId, string code) =>
        await users.VerifyTwoFactorTokenAsync(await GetAsync(userId), users.Options.Tokens.AuthenticatorTokenProvider, code);

    public async Task SetMfaEnabledAsync(Guid userId, bool enabled)
    {
        var user = await GetAsync(userId);
        await users.SetTwoFactorEnabledAsync(user, enabled);
        if (!enabled) await users.ResetAuthenticatorKeyAsync(user);
    }

    public async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(Guid userId) =>
        (await users.GenerateNewTwoFactorRecoveryCodesAsync(await GetAsync(userId), 10))?.ToList() ?? [];

    public async Task RevokeSessionsAsync(Guid userId) => await users.UpdateSecurityStampAsync(await GetAsync(userId));

    public async Task AnonymizeAsync(Guid userId)
    {
        var user = await GetAsync(userId);
        var placeholder = $"deleted-{user.Id:N}@deleted.invalid";
        user.UserName = placeholder;
        user.Email = placeholder;
        user.FullName = "Deleted user";
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.PasswordHash = null;
        user.TwoFactorEnabled = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        await users.UpdateNormalizedUserNameAsync(user);
        await users.UpdateNormalizedEmailAsync(user);
        await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
        await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
        foreach (var login in await users.GetLoginsAsync(user)) await users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        await users.UpdateSecurityStampAsync(user);
    }

    private async Task<ApplicationUser> GetAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("User not found.");

    private async Task<UserAccount> ToAccountAsync(ApplicationUser user) => new(
        user.Id, user.Email!, user.FullName, user.EmailConfirmed, user.TwoFactorEnabled, (await users.GetRolesAsync(user)).Order().ToList(),
        user.CreatedAt);

    private static Error ToError(IdentityResult result) =>
        result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal))
            ? IdentityErrors.PasswordRejected(string.Join(" ", result.Errors.Select(e => e.Description)))
            : Error.Validation("ACCOUNT_REJECTED", string.Join(" ", result.Errors.Select(e => e.Description)));

    private static string FormatKey(string key) =>
        string.Join(' ', key.ToLowerInvariant().Chunk(4).Select(c => new string(c)));

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string? Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return null; }
    }
}
