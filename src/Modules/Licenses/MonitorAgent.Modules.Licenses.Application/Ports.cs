using MonitorAgent.Modules.Licenses.Domain;

namespace MonitorAgent.Modules.Licenses.Application;

public sealed record PlatformPlan(
    Guid Id, string ProductCode, string ProductName, string Code, string Name, int Version, decimal Price, string Currency,
    int? DurationDays, int? MaxActivations, IReadOnlyList<string> Features);

public sealed record PlatformLicense(
    Guid Id, Guid CustomerId, string ProductCode, string PlanCode, string LicenseNumber, string ProductKeyPrefix, string Status,
    DateTimeOffset IssuedAt, DateTimeOffset? ExpiresAt, int? MaxActivations, int ActiveActivations);

public sealed record PlatformDevice(
    Guid Id, string DeviceId, string? DeviceName, string? OperatingSystem, string? AppVersion, string Status,
    DateTimeOffset ActivatedAt, DateTimeOffset? LastHeartbeatAt, bool Online);

public sealed record PlatformIssuedLicense(PlatformLicense License, string ProductKey);

/// <summary>Thrown by <see cref="ILicensingPlatform"/> when LicensingPlatform can't be reached or answers with a server error.</summary>
public sealed class PlatformUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The anti-corruption layer over LicensingPlatform's HTTP API (ADR-004, ADR-007). Nothing outside this module
/// sees LicensingPlatform's shapes. Lookups return null for "not found".
/// </summary>
internal interface ILicensingPlatform
{
    Task<IReadOnlyList<PlatformPlan>> PublishedPaidPlansAsync(CancellationToken ct);
    Task<Guid> CreateCustomerAsync(string name, string email, CancellationToken ct);
    Task<Guid> StartSubscriptionAsync(Guid customerId, Guid planId, CancellationToken ct);
    Task<PlatformIssuedLicense> IssueLicenseAsync(Guid subscriptionId, CancellationToken ct);
    Task<IReadOnlyList<PlatformLicense>> LicensesOfAsync(Guid customerId, CancellationToken ct);
    Task<(PlatformLicense License, IReadOnlyList<PlatformDevice> Devices)?> LicenseAsync(Guid licenseId, CancellationToken ct);
    Task ResetDeviceAsync(Guid licenseId, Guid deviceId, CancellationToken ct);
}

internal interface ILicenseStore
{
    Task<PlatformCustomerLink?> LinkForAsync(Guid companyId, CancellationToken ct);
    void Add(PlatformCustomerLink link);
    Task<LicenseGrant?> GrantForOrderAsync(string orderReference, CancellationToken ct);
    Task<LicenseGrant?> GrantForLicenseAsync(Guid companyId, Guid platformLicenseId, CancellationToken ct);
    /// <summary>Issued grants expiring between now and <paramref name="until"/>.</summary>
    Task<IReadOnlyList<LicenseGrant>> ExpiringAsync(DateTimeOffset now, DateTimeOffset until, int take, CancellationToken ct);
    /// <summary>Whether the company holds another issued license on the same plan that lasts longer: then it already renewed.</summary>
    Task<bool> RenewedAsync(LicenseGrant grant, CancellationToken ct);
    void Add(LicenseGrant grant);
    Task SaveAsync(CancellationToken ct);
}

/// <summary>Encrypts product keys at rest.</summary>
internal interface IKeyProtector
{
    string Protect(string productKey);
    string Unprotect(string protectedKey);
}
