using Microsoft.Extensions.Logging;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Licenses.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Application;

/// <summary>Emails company owners whose licenses expire within 14 and again within 3 days. Run on a timer; safe to run often.</summary>
public sealed record SendExpiryRemindersCommand : ICommand<Result<int>>;

internal sealed partial class SendExpiryRemindersHandler(ILicenseStore store, ICompanyDirectory companies, IUserDirectory users, IEmailSender email,
    PortalLinks links, TimeProvider clock, ILogger<SendExpiryRemindersHandler> logger) : ICommandHandler<SendExpiryRemindersCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(SendExpiryRemindersCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var sent = 0;
        foreach (var grant in await store.ExpiringAsync(now, now.AddDays(LicenseGrant.ReminderWindows.Max()), 200, ct))
        {
            if (grant.DueReminder(now) is not { } days) continue;
            // Marked first and saved per license: a crash after sending can repeat at most one email, never a whole batch.
            grant.ReminderSent(days);
            await store.SaveAsync(ct);
            if (await store.RenewedAsync(grant, ct)) continue;
            if (await companies.FindAsync(grant.CompanyId, ct) is not { } company || await users.FindAsync(company.OwnerUserId, ct) is not { } owner) continue;
            try
            {
                await email.SendAsync(LicenseEmails.Expiring(owner.Email, grant.LicenseNumber!, grant.ExpiresAt!.Value, days,
                    links.To($"checkout?plan={grant.PlanId}")), ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                EmailFailed(logger, ex, grant.Id);
            }
        }
        return sent;
    }

    [LoggerMessage(LogLevel.Warning, "Could not email the expiry reminder for grant {GrantId}")]
    private static partial void EmailFailed(ILogger logger, Exception ex, Guid grantId);
}

internal static class LicenseEmails
{
    public static EmailMessage Expiring(string to, string license, DateTimeOffset expires, int days, string renewLink) => new(to,
        $"الترخيص {license} بينتهي خلال {days} أيام | License {license} expires in {days} days",
        $"""
        الترخيص {license} بينتهي يوم {expires:yyyy-MM-dd} (UTC). بعد كده البرنامج بيشتغل فترة سماح قصيرة وبعدين بيتقفل.
        جدّد من هنا عشان المراقبة ماتقفش:
        {renewLink}

        License {license} expires on {expires:yyyy-MM-dd} (UTC). After that the program runs for a short grace period, then stops.
        Renew here so monitoring doesn't stop:
        {renewLink}
        """);
}
