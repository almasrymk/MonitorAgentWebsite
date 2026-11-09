using MonitorAgent.Modules.Licenses.Domain;

namespace MonitorAgent.Modules.Licenses.Application;

public sealed record PlatformPlan(
    Guid Id, string ProductCode, string ProductName, string Code, string Name, int Version, decimal Price, string Currency,
    int? DurationDays, int? MaxActivations, IReadOnlyList<string> Features);

public sealed record PlatformLicense(
    Guid Id, Guid CustomerId, string ProductCode, string PlanCode, string LicenseNumber, string ProductKeyPrefix, string Status,
    DateTimeOffset IssuedAt, DateTimeOffset? ExpiresAt, int? MaxActivations, int ActiveActivations, Guid SubscriptionId = default);

public sealed record PlatformDevice(
    Guid Id, string DeviceId, string? DeviceName, string? OperatingSystem, string? AppVersion, string Status,
    DateTimeOffset ActivatedAt, DateTimeOffset? LastHeartbeatAt, bool Online);

public sealed record PlatformIssuedLicense(PlatformLicense License, string ProductKey);

public sealed record PlatformSubscription(
    Guid Id, Guid CustomerId, Guid PlanId, string PlanCode, string PlanName, string Status,
    DateTimeOffset StartDate, DateTimeOffset? EndDate, int Version,
    int ActiveDevices = 0, int? MaxDevices = null, DateTimeOffset? OverCapacitySince = null);

/// <summary>A trial code for the Agent (shown once), when it stops working and how long the trial runs.</summary>
public sealed record PlatformTrialGrant(string TrialCode, DateTimeOffset ExpiresAt, string ProductCode, int TrialDays);

/// <summary>LicensingPlatform refused the call with a client error the caller can act on.</summary>
public sealed class PlatformRejectedException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>Thrown by <see cref="ILicensingPlatform"/> when LicensingPlatform can't be reached or answers with a server error.</summary>
public sealed class PlatformUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Thrown by <see cref="IMonitorCloud"/> when Monitor Cloud can't be reached or answers with a server error.</summary>
public sealed class MonitorUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The anti-corruption layer over LicensingPlatform's HTTP API (ADR-004, ADR-007). Nothing outside this module
/// sees LicensingPlatform's shapes. Lookups return null for "not found".
/// </summary>
internal interface ILicensingPlatform
{
    Task<IReadOnlyList<PlatformPlan>> PublishedPaidPlansAsync(CancellationToken ct);
    /// <summary>A repeat with the same <paramref name="externalReference"/> returns the first customer (INT-016).</summary>
    Task<Guid> CreateCustomerAsync(string name, string email, string externalReference, CancellationToken ct);
    /// <summary>A repeat with the same <paramref name="externalReference"/> returns the first subscription (INT-016).</summary>
    Task<Guid> StartSubscriptionAsync(Guid customerId, Guid planId, string externalReference, CancellationToken ct);
    Task<PlatformIssuedLicense> IssueLicenseAsync(Guid subscriptionId, CancellationToken ct);
    Task<IReadOnlyList<PlatformSubscription>> SubscriptionsOfAsync(Guid customerId, CancellationToken ct);
    Task ChangePlanAsync(Guid subscriptionId, Guid planId, CancellationToken ct);
    /// <summary>INT-018: extends the term once per <paramref name="externalReference"/>; returns the new end date.</summary>
    Task<DateTimeOffset?> RenewSubscriptionAsync(Guid subscriptionId, string externalReference, CancellationToken ct);
    /// <param name="byCustomer">INT-013: the company asked for it in self-service. LicensingPlatform then lets the company
    /// resume it, and refuses a company resume of a suspension the provider made.</param>
    Task SuspendSubscriptionAsync(Guid subscriptionId, string? reason, bool byCustomer, CancellationToken ct);
    Task ResumeSubscriptionAsync(Guid subscriptionId, bool byCustomer, CancellationToken ct);
    Task CancelSubscriptionAsync(Guid subscriptionId, string? reason, CancellationToken ct);
    Task<PlatformIssuedLicense> RegenerateKeyAsync(Guid licenseId, CancellationToken ct);

    /// <summary>B5 (Licensing ADR-016): a single-use trial code for one website account. Needs the API client's trials.grant scope.</summary>
    Task<PlatformTrialGrant> IssueTrialGrantAsync(string productCode, string reference, CancellationToken ct);
    Task SuspendLicenseAsync(Guid licenseId, string? reason, bool byCustomer, CancellationToken ct);
    Task ResumeLicenseAsync(Guid licenseId, bool byCustomer, CancellationToken ct);
    Task RevokeLicenseAsync(Guid licenseId, string? reason, CancellationToken ct);
    Task<IReadOnlyList<PlatformLicense>> LicensesOfAsync(Guid customerId, CancellationToken ct);
    Task<(PlatformLicense License, IReadOnlyList<PlatformDevice> Devices)?> LicenseAsync(Guid licenseId, CancellationToken ct);
    Task ResetDeviceAsync(Guid licenseId, Guid deviceId, CancellationToken ct);
}

/// <summary>
/// Registers a Licensing customer as a tenant in Monitor Cloud. The same customer always returns the same tenant.
/// </summary>
internal interface IMonitorCloud
{
    /// <param name="owner">The company owner, invited by Monitor Cloud as the first Administrator (Cloud ADR 0018).</param>
    Task<Guid> RegisterTenantAsync(Guid licensingCustomerId, string name, MonitorTenantOwner? owner, CancellationToken ct);
}

/// <summary>The person Monitor Cloud invites as a new tenant's first Administrator.</summary>
public sealed record MonitorTenantOwner(string Email, string FullName);

internal interface ILicenseStore
{
    Task<PlatformCustomerLink?> LinkForAsync(Guid companyId, CancellationToken ct);
    void Add(PlatformCustomerLink link);
    Task<LicenseGrant?> GrantForOrderAsync(string orderReference, CancellationToken ct);
    Task<LicenseGrant?> GrantAsync(Guid grantId, CancellationToken ct);
    Task<LicenseGrant?> GrantForLicenseAsync(Guid companyId, Guid platformLicenseId, CancellationToken ct);
    /// <summary>INT-014: every grant of the company on this subscription (the bought one and any issued later), oldest first.</summary>
    Task<IReadOnlyList<LicenseGrant>> GrantsForSubscriptionAsync(Guid companyId, Guid platformSubscriptionId, CancellationToken ct);
    /// <summary>Issued grants expiring between now and <paramref name="until"/>.</summary>
    Task<IReadOnlyList<LicenseGrant>> ExpiringAsync(DateTimeOffset now, DateTimeOffset until, int take, CancellationToken ct);
    /// <summary>Whether the company holds another issued license on the same plan that lasts longer: then it already renewed.</summary>
    Task<bool> RenewedAsync(LicenseGrant grant, CancellationToken ct);
    void Add(LicenseGrant grant);
    Task<ScheduledPlanChange?> ScheduledChangeAsync(Guid subscriptionId, CancellationToken ct);
    Task<IReadOnlyList<ScheduledPlanChange>> ScheduledChangesOfAsync(Guid companyId, CancellationToken ct);
    void Add(ScheduledPlanChange change);
    void Remove(ScheduledPlanChange change);
    /// <exception cref="GrantConflictException">Another request saved the same grant (or claimed it) first.</exception>
    Task SaveAsync(CancellationToken ct);
}

/// <summary>Thrown by <see cref="ILicenseStore.SaveAsync"/> when a concurrent request won a unique key or a claim.</summary>
public sealed class GrantConflictException(Exception inner) : Exception("A concurrent fulfilment won.", inner);

/// <summary>Encrypts product keys at rest.</summary>
internal interface IKeyProtector
{
    string Protect(string productKey);
    string Unprotect(string protectedKey);
}
