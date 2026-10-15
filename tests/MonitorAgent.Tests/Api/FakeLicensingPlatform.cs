using System.Collections.Concurrent;
using MonitorAgent.Modules.Licenses.Application;

namespace MonitorAgent.Tests.Api;

/// <summary>An in-memory LicensingPlatform with the same rules the real one enforces for these calls.</summary>
public sealed class FakeLicensingPlatform : ILicensingPlatform
{
    public static readonly PlatformPlan Standard = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "UBG", "UBG Monitor",
        "STD", "Standard", 1, 1200m, "EGP", 365, 3, ["alerts", "reports"]);

    public ConcurrentDictionary<Guid, string> Customers { get; } = new();
    public ConcurrentDictionary<Guid, (Guid CustomerId, Guid PlanId)> Subscriptions { get; } = new();
    public ConcurrentDictionary<Guid, (PlatformLicense License, string Key, List<PlatformDevice> Devices)> Licenses { get; } = new();

    /// <summary>Name of the next call that should fail as if LicensingPlatform were down.</summary>
    public string? FailNext { get; set; }

    public Task<IReadOnlyList<PlatformPlan>> PublishedPaidPlansAsync(CancellationToken ct)
    {
        Trip(nameof(PublishedPaidPlansAsync));
        return Task.FromResult<IReadOnlyList<PlatformPlan>>([Standard]);
    }

    public Task<Guid> CreateCustomerAsync(string name, string email, CancellationToken ct)
    {
        Trip(nameof(CreateCustomerAsync));
        var id = Guid.NewGuid();
        Customers[id] = name;
        return Task.FromResult(id);
    }

    public Task<Guid> StartSubscriptionAsync(Guid customerId, Guid planId, CancellationToken ct)
    {
        Trip(nameof(StartSubscriptionAsync));
        var id = Guid.NewGuid();
        Subscriptions[id] = (customerId, planId);
        return Task.FromResult(id);
    }

    public Task<PlatformIssuedLicense> IssueLicenseAsync(Guid subscriptionId, CancellationToken ct)
    {
        Trip(nameof(IssueLicenseAsync));
        var subscription = Subscriptions[subscriptionId];
        var key = $"UBG-{Guid.NewGuid():N}".ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var license = new PlatformLicense(Guid.NewGuid(), subscription.CustomerId, "UBG", "STD", $"LIC-{Licenses.Count + 1:D5}", key[..8], "Active",
            now, now.AddDays(365), 3, 1);
        var device = new PlatformDevice(Guid.NewGuid(), "device-1", "Front desk PC", "Windows 11", "2.4.0", "Active", now, now, true);
        Licenses[license.Id] = (license, key, [device]);
        return Task.FromResult(new PlatformIssuedLicense(license, key));
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

    private void Trip(string call)
    {
        if (FailNext != call) return;
        FailNext = null;
        throw new PlatformUnavailableException("Simulated outage.");
    }
}
