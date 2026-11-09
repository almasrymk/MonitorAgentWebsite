using MonitorAgent.Modules.Licenses.Contracts;
using MonitorAgent.Modules.Licenses.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Application;

/// <summary>
/// INT-018: a paid renewal extends the subscription behind a license instead of selling a second license, so the
/// product key and the activated devices stay. The price is the plan's current price (its newest published version).
/// </summary>
internal sealed class LicenseRenewals(ILicensingPlatform platform, ILicenseStore store, CatalogCache catalog) : ILicenseRenewals
{
    public async Task<Result<RenewalOffer>> OfferAsync(Guid companyId, Guid licenseId, CancellationToken ct)
    {
        if (await store.GrantForLicenseAsync(companyId, licenseId, ct) is not { } byLicense) return LicensesErrors.LicenseNotFound;
        var grantId = byLicense.Id;
        var found = await RenewableAsync(companyId, grantId, ct);
        if (found.IsFailure) return found.Error!;
        try
        {
            var details = await platform.LicenseAsync(found.Value.PlatformLicenseId!.Value, ct);
            if (details is not { } live || live.License.Status == "Revoked") return LicensesErrors.NotRenewable;
            var plans = await catalog.PlansAsync(ct);
            var scheduled = await store.ScheduledChangeAsync(found.Value.PlatformSubscriptionId!.Value, ct);
            var plan = scheduled is not null
                ? plans.FirstOrDefault(p => p.Id == scheduled.PlanId)
                : plans.FirstOrDefault(p => p.ProductCode == live.License.ProductCode && p.Code == live.License.PlanCode);
            if (plan is null) return LicensesErrors.PlanNotAvailable;
            return new RenewalOffer(grantId, plan.Id, plan.ProductName, plan.Name, plan.Price, plan.Currency);
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }

    public async Task<Result<FulfilledLicense>> RenewAsync(Guid companyId, Guid grantId, Guid planId, string orderReference, CancellationToken ct)
    {
        var found = await RenewableAsync(companyId, grantId, ct);
        if (found.IsFailure) return found.Error!;
        var grant = found.Value;
        try
        {
            var subscriptionId = grant.PlatformSubscriptionId!.Value;
            var endsAt = await platform.RenewSubscriptionAsync(subscriptionId, RenewalReference(orderReference), ct);
            // INT-017: the order was priced at the scheduled plan, so the paid term moves to it. Renewing first makes a
            // lapsed subscription Active again, which ChangePlan requires. A retry finds the plan already applied.
            if (planId != Guid.Empty && planId != grant.PlanId)
            {
                try
                {
                    await platform.ChangePlanAsync(subscriptionId, planId, ct);
                }
                catch (PlatformRejectedException rejected) when (rejected.Code == LicensesErrors.SamePlan.Code)
                {
                }
                grant.MovedToPlan(planId);
            }
            if (await store.ScheduledChangeAsync(subscriptionId, ct) is { } scheduled && scheduled.PlanId == planId) store.Remove(scheduled);
            grant.Renewed(endsAt);
            await store.SaveAsync(ct);
            return new FulfilledLicense(grant.Id, grant.LicenseNumber!, grant.ExpiresAt);
        }
        catch (PlatformUnavailableException)
        {
            // LicensingPlatform applies the renewal once per reference, so the retry worker can simply call again.
            return LicensesErrors.PlatformUnavailable;
        }
        catch (PlatformRejectedException)
        {
            return LicensesErrors.NotRenewable;
        }
        catch (GrantConflictException)
        {
            return LicensesErrors.IssueInProgress;
        }
    }

    internal static string RenewalReference(string orderReference) => $"website-renewal:{orderReference}";

    private async Task<Result<LicenseGrant>> RenewableAsync(Guid companyId, Guid grantId, CancellationToken ct)
    {
        var grant = await store.GrantAsync(grantId, ct);
        if (grant is null || grant.CompanyId != companyId) return LicensesErrors.LicenseNotFound;
        if (grant.Status != GrantStatus.Issued || grant.PlatformSubscriptionId is null || grant.PlatformLicenseId is null) return LicensesErrors.NotRenewable;
        return grant;
    }
}
