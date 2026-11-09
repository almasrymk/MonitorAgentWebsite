using MonitorAgent.Modules.Licenses.Contracts;
using MonitorAgent.Modules.Licenses.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Application;

/// <summary>INT-014 (ADR-020): the prorated price of an upgrade, kept apart so the rounding rules are tested on their own.</summary>
public static class Proration
{
    /// <summary>
    /// Whole days left in the term, counted down (a part day is free to the customer), never more than the term.
    /// </summary>
    public static int RemainingDays(DateTimeOffset termEndsAt, DateTimeOffset now, int termDays) =>
        Math.Clamp((int)Math.Floor((termEndsAt - now).TotalDays), 0, termDays);

    /// <summary>
    /// (new - current) x remaining / term, rounded half away from zero to two decimals, at least 0.01. No tax rule is
    /// applied: the difference of two published list prices is charged as it is.
    /// </summary>
    public static decimal UpgradePrice(decimal currentPrice, decimal newPrice, int remainingDays, int termDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(termDays);
        if (newPrice <= currentPrice) throw new ArgumentException("An upgrade costs more than the current plan.", nameof(newPrice));
        if (remainingDays <= 0 || remainingDays > termDays) throw new ArgumentOutOfRangeException(nameof(remainingDays));
        var price = decimal.Round((newPrice - currentPrice) * remainingDays / termDays, 2, MidpointRounding.AwayFromZero);
        return Math.Max(price, 0.01m);
    }
}

/// <summary>
/// INT-014: a paid, prorated move to a dearer plan in the middle of a term. The quote is priced from the catalog now;
/// the order keeps that price. The plan changes only once the order is paid, through LicensingPlatform's ChangePlan,
/// which keeps the term end date and moves every license of the subscription to the new plan's entitlements.
/// </summary>
internal sealed class LicenseUpgrades(ILicensingPlatform platform, ILicenseStore store, CatalogCache catalog, TimeProvider clock) : ILicenseUpgrades
{
    public async Task<Result<UpgradeOffer>> QuoteAsync(Guid companyId, Guid subscriptionId, Guid planId, CancellationToken ct)
    {
        var grant = (await store.GrantsForSubscriptionAsync(companyId, subscriptionId, ct)).FirstOrDefault(g => g.Status == GrantStatus.Issued);
        if (grant is null || await store.LinkForAsync(companyId, ct) is not { } link)
            return LicensesErrors.SubscriptionNotFound;
        try
        {
            var subscription = (await platform.SubscriptionsOfAsync(link.PlatformCustomerId, ct))
                .FirstOrDefault(s => s.Id == subscriptionId && s.CustomerId == link.PlatformCustomerId);
            if (subscription is null) return LicensesErrors.SubscriptionNotFound;
            var plans = await catalog.PlansAsync(ct);
            if (plans.FirstOrDefault(p => p.Id == planId) is not { } target) return LicensesErrors.PlanNotAvailable;
            if (target.Id == subscription.PlanId) return LicensesErrors.SamePlan;
            var current = plans.FirstOrDefault(p => p.Id == subscription.PlanId);
            // Only an active, fixed-term subscription on a plan still on sale has a price to prorate from.
            if (current is null || subscription.Status != "Active" || current.DurationDays is not > 0 || subscription.EndDate is not { } endsAt)
                return LicensesErrors.NotUpgradable;
            if (target.ProductCode != current.ProductCode || target.Currency != current.Currency || target.Price <= current.Price)
                return LicensesErrors.NotAnUpgrade;
            var term = current.DurationDays.Value;
            var remaining = Proration.RemainingDays(endsAt, clock.GetUtcNow(), term);
            if (remaining == 0) return LicensesErrors.UpgradeTermEnding;
            return new UpgradeOffer(grant.Id, subscription.Id, current.Id, target.Id, target.ProductName, current.Name, target.Name,
                Proration.UpgradePrice(current.Price, target.Price, remaining, term), target.Currency, remaining, term, endsAt);
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }

    public async Task<Result<FulfilledLicense>> UpgradeAsync(Guid companyId, Guid grantId, Guid planId, string orderReference, CancellationToken ct)
    {
        var grant = await store.GrantAsync(grantId, ct);
        if (grant is null || grant.CompanyId != companyId) return LicensesErrors.LicenseNotFound;
        if (grant.Status != GrantStatus.Issued || grant.PlatformSubscriptionId is not { } subscriptionId) return LicensesErrors.NotUpgradable;
        try
        {
            try
            {
                await platform.ChangePlanAsync(subscriptionId, planId, ct);
            }
            catch (PlatformRejectedException rejected) when (rejected.Code == LicensesErrors.SamePlan.Code)
            {
                // A retry after a lost answer: the plan is already applied.
            }
            // LicensingPlatform moves every license of the subscription, so every grant on it records the new plan.
            foreach (var each in await store.GrantsForSubscriptionAsync(companyId, subscriptionId, ct)) each.MovedToPlan(planId);
            // The paid upgrade replaces a cheaper plan scheduled for the renewal: the customer chose the dearer one.
            if (await store.ScheduledChangeAsync(subscriptionId, ct) is { } scheduled) store.Remove(scheduled);
            await store.SaveAsync(ct);
            return new FulfilledLicense(grant.Id, grant.LicenseNumber!, grant.ExpiresAt);
        }
        catch (PlatformUnavailableException)
        {
            // ChangePlan to the same plan is answered as already applied, so the retry worker can call again.
            return LicensesErrors.PlatformUnavailable;
        }
        catch (PlatformRejectedException)
        {
            // Suspended, cancelled or expired since the order was placed: the order stays Paid for staff (refund or apply by hand).
            return LicensesErrors.NotUpgradable;
        }
        catch (GrantConflictException)
        {
            return LicensesErrors.IssueInProgress;
        }
    }
}
