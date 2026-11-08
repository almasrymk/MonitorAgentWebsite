using System.Collections.Concurrent;
using MonitorAgent.Modules.Licenses.Application;

namespace MonitorAgent.Tests.Api;

/// <summary>An in-memory LicensingPlatform with the same rules the real one enforces for these calls.</summary>
public sealed class FakeLicensingPlatform : ILicensingPlatform
{
    public static readonly PlatformPlan Standard = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "UBG", "UBG Monitor",
        "STD", "Standard", 1, 1200m, "EGP", 365, 3, ["alerts", "reports"]);

    public static readonly PlatformPlan Plus = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "UBG", "UBG Monitor",
        "PLUS", "Plus", 1, 2400m, "EGP", 365, 10, ["alerts", "reports", "archive"]);

    public ConcurrentDictionary<Guid, string> Customers { get; } = new();
    public ConcurrentDictionary<Guid, (Guid CustomerId, Guid PlanId)> Subscriptions { get; } = new();
    public ConcurrentDictionary<Guid, string> SubscriptionStatuses { get; } = new();
    public ConcurrentDictionary<Guid, (PlatformLicense License, string Key, List<PlatformDevice> Devices)> Licenses { get; } = new();

    /// <summary>Name of the next call that should fail as if LicensingPlatform were down.</summary>
    public string? FailNext { get; set; }

    /// <summary>Name of the next call whose work is done but whose answer is lost (a timeout after LicensingPlatform committed).</summary>
    public string? LoseAnswerOf { get; set; }

    /// <summary>The external references LicensingPlatform keeps unique (INT-016).</summary>
    public ConcurrentDictionary<string, Guid> References { get; } = new();

    private int _issueCalls;

    public int IssueCalls => _issueCalls;

    public Task<IReadOnlyList<PlatformPlan>> PublishedPaidPlansAsync(CancellationToken ct)
    {
        Trip(nameof(PublishedPaidPlansAsync));
        return Task.FromResult<IReadOnlyList<PlatformPlan>>([Standard, Plus]);
    }

    public Task<Guid> CreateCustomerAsync(string name, string email, string externalReference, CancellationToken ct)
    {
        Trip(nameof(CreateCustomerAsync));
        var id = References.GetOrAdd("customer:" + externalReference, _ =>
        {
            var created = Guid.NewGuid();
            Customers[created] = name;
            return created;
        });
        return Answer(nameof(CreateCustomerAsync), id);
    }

    public Task<Guid> StartSubscriptionAsync(Guid customerId, Guid planId, string externalReference, CancellationToken ct)
    {
        Trip(nameof(StartSubscriptionAsync));
        var id = References.GetOrAdd("subscription:" + externalReference, _ =>
        {
            var created = Guid.NewGuid();
            Subscriptions[created] = (customerId, planId);
            SubscriptionStatuses[created] = "Active";
            return created;
        });
        return Answer(nameof(StartSubscriptionAsync), id);
    }

    public Task<PlatformIssuedLicense> IssueLicenseAsync(Guid subscriptionId, CancellationToken ct)
    {
        Trip(nameof(IssueLicenseAsync));
        Interlocked.Increment(ref _issueCalls);
        var subscription = Subscriptions[subscriptionId];
        var plan = Plan(subscription.PlanId);
        var key = $"UBG-{Guid.NewGuid():N}".ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var license = new PlatformLicense(Guid.NewGuid(), subscription.CustomerId, plan.ProductCode, plan.Code, $"LIC-{Licenses.Count + 1:D5}", key[..8], "Active",
            now, now.AddDays(plan.DurationDays ?? 365), plan.MaxActivations, 1, subscriptionId);
        var device = new PlatformDevice(Guid.NewGuid(), "device-1", "Front desk PC", "Windows 11", "2.4.0", "Active", now, now, true);
        Licenses[license.Id] = (license, key, [device]);
        return Answer(nameof(IssueLicenseAsync), new PlatformIssuedLicense(license, key));
    }

    public Task<IReadOnlyList<PlatformLicense>> LicensesOfAsync(Guid customerId, CancellationToken ct)
    {
        Trip(nameof(LicensesOfAsync));
        return Task.FromResult<IReadOnlyList<PlatformLicense>>(Licenses.Values.Where(l => l.License.CustomerId == customerId).Select(l => l.License).ToList());
    }

    public Task<(PlatformLicense License, IReadOnlyList<PlatformDevice> Devices)?> LicenseAsync(Guid licenseId, CancellationToken ct)
    {
        Trip(nameof(LicenseAsync));
        return Task.FromResult<(PlatformLicense, IReadOnlyList<PlatformDevice>)?>(
            Licenses.TryGetValue(licenseId, out var l) ? (l.License, l.Devices.ToList()) : null);
    }

    public Task ResetDeviceAsync(Guid licenseId, Guid deviceId, CancellationToken ct)
    {
        Trip(nameof(ResetDeviceAsync));
        var devices = Licenses[licenseId].Devices;
        var index = devices.FindIndex(d => d.Id == deviceId);
        devices[index] = devices[index] with { Status = "Deactivated", Online = false };
        return Task.CompletedTask;
    }

    public string KeyOf(Guid licenseId) => Licenses[licenseId].Key;

    public Task<IReadOnlyList<PlatformSubscription>> SubscriptionsOfAsync(Guid customerId, CancellationToken ct)
    {
        Trip(nameof(SubscriptionsOfAsync));
        IReadOnlyList<PlatformSubscription> list = Subscriptions.Where(s => s.Value.CustomerId == customerId).Select(s =>
        {
            var plan = Plan(s.Value.PlanId);
            return new PlatformSubscription(s.Key, customerId, plan.Id, plan.Code, plan.Name, SubscriptionStatuses.GetValueOrDefault(s.Key, "Active"),
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(plan.DurationDays ?? 365), 1);
        }).ToList();
        return Task.FromResult(list);
    }

    public Task ChangePlanAsync(Guid subscriptionId, Guid planId, CancellationToken ct)
    {
        Trip(nameof(ChangePlanAsync));
        if (!Subscriptions.TryGetValue(subscriptionId, out var subscription))
            throw new PlatformRejectedException(404, "NOT_FOUND", "Subscription was not found.");
        if (PlanOrNull(planId) is null)
            throw new PlatformRejectedException(404, "NOT_FOUND", "Plan was not found.");
        if (subscription.PlanId == planId)
            throw new PlatformRejectedException(409, "SUBSCRIPTION_SAME_PLAN", "Subscription is already on this plan.");
        if (SubscriptionStatuses.GetValueOrDefault(subscriptionId, "Active") is not ("Active" or "Trial"))
            throw new PlatformRejectedException(409, "SUBSCRIPTION_INVALID_TRANSITION", "This action is not allowed.");
        Subscriptions[subscriptionId] = (subscription.CustomerId, planId);
        return Task.CompletedTask;
    }

    /// <summary>Renewals applied, by reference: LicensingPlatform extends a term once per reference.</summary>
    public ConcurrentDictionary<string, Guid> Renewals { get; } = new();

    public Task<DateTimeOffset?> RenewSubscriptionAsync(Guid subscriptionId, string externalReference, CancellationToken ct)
    {
        Trip(nameof(RenewSubscriptionAsync));
        var plan = Plan(Subscriptions[subscriptionId].PlanId);
        if (Renewals.TryAdd(externalReference, subscriptionId))
        {
            foreach (var (id, entry) in Licenses.Where(l => l.Value.License.SubscriptionId == subscriptionId).ToList())
                Licenses[id] = (entry.License with { ExpiresAt = (entry.License.ExpiresAt ?? DateTimeOffset.UtcNow).AddDays(plan.DurationDays ?? 365) }, entry.Key, entry.Devices);
        }

        var end = Licenses.Values.Where(l => l.License.SubscriptionId == subscriptionId).Select(l => l.License.ExpiresAt).Max();
        return Answer(nameof(RenewSubscriptionAsync), end);
    }

    /// <summary>INT-013: what the company suspended itself (LicensingPlatform's SuspendedByCustomer).</summary>
    public ConcurrentDictionary<Guid, bool> SuspendedByCustomer { get; } = new();

    public Task SuspendSubscriptionAsync(Guid subscriptionId, string? reason, bool byCustomer, CancellationToken ct)
    {
        SetSubscriptionAsync(subscriptionId, "Suspended", "Active", "Trial");
        SuspendedByCustomer[subscriptionId] = byCustomer;
        return Task.CompletedTask;
    }

    public Task ResumeSubscriptionAsync(Guid subscriptionId, bool byCustomer, CancellationToken ct)
    {
        if (byCustomer && SubscriptionStatuses.GetValueOrDefault(subscriptionId) == "Suspended" && !SuspendedByCustomer.GetValueOrDefault(subscriptionId))
            throw new PlatformRejectedException(409, "SUBSCRIPTION_SUSPENDED_BY_PROVIDER", "This subscription was suspended by your provider.");
        SuspendedByCustomer.TryRemove(subscriptionId, out _);
        return SetSubscriptionAsync(subscriptionId, "Active", "Suspended");
    }
    public Task CancelSubscriptionAsync(Guid subscriptionId, string? reason, CancellationToken ct) => SetSubscriptionAsync(subscriptionId, "Cancelled", "Active", "Trial", "Suspended");

    /// <summary>Trial codes by reference; a reference whose trial was used (see <see cref="UsedTrials"/>) is refused like LicensingPlatform.</summary>
    public ConcurrentDictionary<string, PlatformTrialGrant> TrialGrants { get; } = new();
    public ConcurrentDictionary<string, bool> UsedTrials { get; } = new();

    public Task<PlatformTrialGrant> IssueTrialGrantAsync(string productCode, string reference, CancellationToken ct)
    {
        Trip(nameof(IssueTrialGrantAsync));
        if (UsedTrials.ContainsKey(reference))
            throw new PlatformRejectedException(409, "LIC_TRIAL_GRANT_USED", "A trial of this product was already started for this account.");
        var grant = new PlatformTrialGrant($"ltg_{Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddHours(24), productCode, 14);
        TrialGrants[reference] = grant;
        return Task.FromResult(grant);
    }

    public Task<PlatformIssuedLicense> RegenerateKeyAsync(Guid licenseId, CancellationToken ct)
    {
        Trip(nameof(RegenerateKeyAsync));
        var current = Licenses[licenseId];
        var key = $"UBG-{Guid.NewGuid():N}".ToUpperInvariant();
        var license = current.License with { ProductKeyPrefix = key[..8], ActiveActivations = 0 };
        Licenses[licenseId] = (license, key, []);
        return Task.FromResult(new PlatformIssuedLicense(license, key));
    }

    public Task SuspendLicenseAsync(Guid licenseId, string? reason, bool byCustomer, CancellationToken ct)
    {
        SuspendedByCustomer[licenseId] = byCustomer;
        return SetLicenseAsync(licenseId, "Suspended");
    }

    public Task ResumeLicenseAsync(Guid licenseId, bool byCustomer, CancellationToken ct)
    {
        if (byCustomer && Licenses[licenseId].License.Status == "Suspended" && !SuspendedByCustomer.GetValueOrDefault(licenseId))
            throw new PlatformRejectedException(409, "LICENSE_SUSPENDED_BY_PROVIDER", "This license was suspended by your provider.");
        SuspendedByCustomer.TryRemove(licenseId, out _);
        return SetLicenseAsync(licenseId, "Active");
    }

    /// <summary>A suspension made by the provider's staff in LicensingPlatform, outside the website.</summary>
    public void ProviderSuspendsSubscription(Guid subscriptionId)
    {
        SubscriptionStatuses[subscriptionId] = "Suspended";
        SuspendedByCustomer[subscriptionId] = false;
    }

    public void ProviderSuspendsLicense(Guid licenseId)
    {
        SuspendedByCustomer[licenseId] = false;
        SetLicenseAsync(licenseId, "Suspended");
    }
    public Task RevokeLicenseAsync(Guid licenseId, string? reason, CancellationToken ct) => SetLicenseAsync(licenseId, "Revoked");

    private Task SetSubscriptionAsync(Guid subscriptionId, string status, params string[] from)
    {
        if (!SubscriptionStatuses.TryGetValue(subscriptionId, out var current) || !from.Contains(current))
            throw new PlatformRejectedException(409, "SUBSCRIPTION_INVALID_TRANSITION", "This action is not allowed.");
        SubscriptionStatuses[subscriptionId] = status;
        return Task.CompletedTask;
    }

    private Task SetLicenseAsync(Guid licenseId, string status)
    {
        var current = Licenses[licenseId];
        Licenses[licenseId] = (current.License with { Status = status }, current.Key, current.Devices);
        return Task.CompletedTask;
    }

    private static PlatformPlan Plan(Guid planId) => PlanOrNull(planId) ?? Standard;
    private static PlatformPlan? PlanOrNull(Guid planId) => planId == Plus.Id ? Plus : planId == Standard.Id ? Standard : null;

    private Task<T> Answer<T>(string call, T value)
    {
        if (LoseAnswerOf != call) return Task.FromResult(value);
        LoseAnswerOf = null;
        throw new PlatformUnavailableException("Simulated timeout after the work was done.");
    }

    private void Trip(string call)
    {
        if (FailNext != call) return;
        FailNext = null;
        throw new PlatformUnavailableException("Simulated outage.");
    }
}

/// <summary>Records tenant registrations the website asks Monitor Cloud for. The same customer returns the same tenant.</summary>
public sealed class FakeMonitorCloud : IMonitorCloud
{
    public ConcurrentDictionary<Guid, Guid> Tenants { get; } = new();
    public List<string> Names { get; } = [];
    public List<MonitorTenantOwner?> Owners { get; } = [];

    /// <summary>Name of the next call that should fail as if Monitor Cloud were down.</summary>
    public string? FailNext { get; set; }

    public Task<Guid> RegisterTenantAsync(Guid licensingCustomerId, string name, MonitorTenantOwner? owner, CancellationToken ct)
    {
        if (FailNext == nameof(RegisterTenantAsync))
        {
            FailNext = null;
            throw new MonitorUnavailableException("Simulated outage.");
        }

        Names.Add(name);
        Owners.Add(owner);
        return Task.FromResult(Tenants.GetOrAdd(licensingCustomerId, _ => Guid.NewGuid()));
    }
}
