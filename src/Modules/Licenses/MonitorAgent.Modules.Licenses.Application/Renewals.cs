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
            var plan = (await catalog.PlansAsync(ct)).FirstOrDefault(p => p.ProductCode == live.License.ProductCode && p.Code == live.License.PlanCode);
            if (plan is null) return LicensesErrors.PlanNotAvailable;
            return new RenewalOffer(grantId, plan.ProductName, plan.Name, plan.Price, plan.Currency);
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }

    public async Task<Result<FulfilledLicense>> RenewAsync(Guid companyId, Guid grantId, string orderReference, CancellationToken ct)
    {
        var found = await RenewableAsync(companyId, grantId, ct);
        if (found.IsFailure) return found.Error!;
        var grant = found.Value;
        try
        {
            var endsAt = await platform.RenewSubscriptionAsync(grant.PlatformSubscriptionId!.Value, RenewalReference(orderReference), ct);
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
