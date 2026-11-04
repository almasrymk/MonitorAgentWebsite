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

/// <summary>INT-018: what renewing one of the company's licenses costs now (the current price of its plan).</summary>
public sealed record RenewalOffer(Guid GrantId, string ProductName, string PlanName, decimal Price, string Currency);

/// <summary>
/// INT-018: paid renewal of the subscription behind a license the website issued. The product key and the activated
/// devices stay the same. <see cref="RenewAsync"/> is safe to call again with the same order reference: the term is
/// extended once.
/// </summary>
public interface ILicenseRenewals
{
    /// <param name="licenseId">The license id the portal shows (LicensingPlatform's).</param>
    Task<Result<RenewalOffer>> OfferAsync(Guid companyId, Guid licenseId, CancellationToken ct);

    Task<Result<FulfilledLicense>> RenewAsync(Guid companyId, Guid grantId, string orderReference, CancellationToken ct);
}
