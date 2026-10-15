using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Domain;

/// <summary>Our company's customer record in LicensingPlatform, created the first time the company gets a license.</summary>
public sealed class PlatformCustomerLink
{
    private PlatformCustomerLink() { }

    public PlatformCustomerLink(Guid companyId, Guid platformCustomerId, DateTimeOffset at)
    {
        CompanyId = companyId;
        PlatformCustomerId = platformCustomerId;
        LinkedAt = at;
    }

    public Guid CompanyId { get; private set; }
    public Guid PlatformCustomerId { get; private set; }
    public DateTimeOffset LinkedAt { get; private set; }
}

public enum GrantStatus { Pending, Issued }

public sealed record LicenseGranted(Guid GrantId, Guid CompanyId, string LicenseNumber) : DomainEvent;

/// <summary>
/// One order's license, issued in LicensingPlatform in steps (subscription, then license). Each finished step is
/// saved, so a call that failed half way resumes from where it stopped instead of starting a second subscription.
/// The order reference is unique: it is what makes fulfilment safe to retry.
/// </summary>
public sealed class LicenseGrant : AggregateRoot
{
    private LicenseGrant() { }

    public Guid CompanyId { get; private set; }
    public Guid PlanId { get; private set; }
    public string OrderReference { get; private set; } = "";
    public GrantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? PlatformSubscriptionId { get; private set; }
    public Guid? PlatformLicenseId { get; private set; }
    public string? LicenseNumber { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    /// <summary>The product key, encrypted with the app's data-protection keys. LicensingPlatform only returns it once.</summary>
    public string? ProtectedProductKey { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    /// <summary>The smallest reminder window (days before expiry) already emailed, so each reminder goes out once.</summary>
    public int? ExpiryReminderDays { get; private set; }

    /// <summary>Days before expiry when the owner is reminded to renew.</summary>
    public static readonly int[] ReminderWindows = [14, 3];

    /// <summary>The reminder window due now, if one is due and not sent yet: the tightest window the license has entered.</summary>
    public int? DueReminder(DateTimeOffset now)
    {
        if (Status != GrantStatus.Issued || ExpiresAt is not { } expires || expires <= now) return null;
        var left = expires - now;
        int? due = null;
        foreach (var days in ReminderWindows)
            if (left <= TimeSpan.FromDays(days) && (ExpiryReminderDays is null || days < ExpiryReminderDays)) due = days;
        return due;
    }

    public void ReminderSent(int days) => ExpiryReminderDays = days;

    public static LicenseGrant Start(Guid companyId, Guid planId, string orderReference, DateTimeOffset at) => new()
    {
        CompanyId = companyId,
        PlanId = planId,
        OrderReference = Guard.NotEmpty(orderReference, "Order reference", 100),
        CreatedAt = at,
    };

    public bool IsFor(Guid companyId, Guid planId) => CompanyId == companyId && PlanId == planId;

    public void SubscriptionStarted(Guid subscriptionId) => PlatformSubscriptionId ??= subscriptionId;

    public void Issued(Guid licenseId, string licenseNumber, DateTimeOffset? expiresAt, string protectedProductKey, DateTimeOffset at)
    {
        if (Status == GrantStatus.Issued) return;
        PlatformLicenseId = licenseId;
        LicenseNumber = licenseNumber;
        ExpiresAt = expiresAt;
        ProtectedProductKey = protectedProductKey;
        IssuedAt = at;
        Status = GrantStatus.Issued;
        Raise(new LicenseGranted(Id, CompanyId, licenseNumber));
    }
}
