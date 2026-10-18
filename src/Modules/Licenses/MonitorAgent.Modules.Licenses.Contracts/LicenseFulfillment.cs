using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Contracts;

/// <summary>A plan as it is sold now: what Commerce prices an order from.</summary>
public sealed record PlanOffer(Guid PlanId, string ProductName, string PlanName, decimal Price, string Currency, int? DurationDays);

public interface IPlanCatalog
{
    /// <summary>The plan as sold now; PLAN_NOT_AVAILABLE if it isn't on sale, LICENSING_UNAVAILABLE if the catalog can't be read.</summary>
    Task<Result<PlanOffer>> OfferAsync(Guid planId, CancellationToken ct);
}

public sealed record FulfilledLicense(Guid GrantId, string LicenseNumber, DateTimeOffset? ExpiresAt);

/// <summary>
/// Issues the license a paid order bought. Safe to call again with the same <paramref name="orderReference"/>
/// (a retried payment callback): it resumes or returns the license already issued, never a second one.
/// </summary>
public interface ILicenseFulfillment
{
    Task<Result<FulfilledLicense>> FulfillAsync(Guid companyId, Guid planId, string orderReference, CancellationToken ct);
}
