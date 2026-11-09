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

/// <summary>
/// INT-018: what renewing one of the company's licenses costs now: the current price of its plan, or of the plan a
/// scheduled change moves it to (INT-017). <see cref="PlanId"/> is the plan the renewal is for.
/// </summary>
public sealed record RenewalOffer(Guid GrantId, Guid PlanId, string ProductName, string PlanName, decimal Price, string Currency);

/// <summary>
/// INT-018: paid renewal of the subscription behind a license the website issued. The product key and the activated
/// devices stay the same. <see cref="RenewAsync"/> is safe to call again with the same order reference: the term is
/// extended once.
/// </summary>
public interface ILicenseRenewals
{
    /// <param name="licenseId">The license id the portal shows (LicensingPlatform's).</param>
    Task<Result<RenewalOffer>> OfferAsync(Guid companyId, Guid licenseId, CancellationToken ct);

    /// <param name="planId">The plan the renewal was priced at. When it differs from the subscription's plan, the
    /// subscription moves to it after the term is extended.</param>
    Task<Result<FulfilledLicense>> RenewAsync(Guid companyId, Guid grantId, Guid planId, string orderReference, CancellationToken ct);
}

/// <summary>
/// INT-014: the price of moving a subscription to a dearer plan for the rest of its paid term. <see cref="Price"/> is
/// (new price - current price) x <see cref="RemainingDays"/> / <see cref="TermDays"/>, rounded half away from zero to
/// two decimals, never below 0.01. Both prices are the catalog's current list prices, in the same currency. No tax is
/// added or removed: the amount is charged exactly as the catalog prices are published (ADR-020 in docs/adr/README.md).
/// </summary>
public sealed record UpgradeOffer(Guid GrantId, Guid SubscriptionId, Guid FromPlanId, Guid PlanId, string ProductName, string FromPlanName,
    string PlanName, decimal Price, string Currency, int RemainingDays, int TermDays, DateTimeOffset TermEndsAt);

/// <summary>
/// INT-014: a paid, prorated upgrade. The plan changes only after the order is paid (and, for a bank transfer, approved
/// by staff); the term end date, the product key and the activated devices stay the same. <see cref="UpgradeAsync"/> is
/// safe to call again for the same order: a subscription already on the plan counts as upgraded.
/// </summary>
public interface ILicenseUpgrades
{
    /// <param name="subscriptionId">The LicensingPlatform subscription the portal shows.</param>
    Task<Result<UpgradeOffer>> QuoteAsync(Guid companyId, Guid subscriptionId, Guid planId, CancellationToken ct);

    Task<Result<FulfilledLicense>> UpgradeAsync(Guid companyId, Guid grantId, Guid planId, string orderReference, CancellationToken ct);
}
