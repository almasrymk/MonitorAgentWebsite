using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Licenses.Contracts;
using MonitorAgent.Modules.Licenses.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Application;

/// <summary>Issues the license an order paid for. Commerce calls it after payment; staff call it for manual payments.</summary>
public sealed record FulfillLicenseCommand(Guid CompanyId, Guid PlanId, string OrderReference) : ICommand<Result<FulfilledLicense>>, IAuditedCommand
{
    public string AuditAction => "license.issue";
    public string AuditTarget => $"company:{CompanyId}";
    public string? AuditDetail => $"plan {PlanId}, reference {OrderReference}";
}

internal sealed class FulfillLicenseValidator : AbstractValidator<FulfillLicenseCommand>
{
    public FulfillLicenseValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.OrderReference).NotEmpty().MaximumLength(100);
    }
}

internal sealed class FulfillLicenseHandler(
    ILicensingPlatform platform, IMonitorCloud monitor, ILicenseStore store, IKeyProtector keys, CatalogCache catalog,
    ICompanyDirectory companies, IUserDirectory users, TimeProvider clock)
    : ICommandHandler<FulfillLicenseCommand, Result<FulfilledLicense>>
{
    public async Task<Result<FulfilledLicense>> HandleAsync(FulfillLicenseCommand command, CancellationToken ct)
    {
        var grant = await store.GrantForOrderAsync(command.OrderReference, ct);
        if (grant is not null && !grant.IsFor(command.CompanyId, command.PlanId)) return LicensesErrors.OrderMismatch;

        try
        {
            if (grant is { Status: GrantStatus.Issued })
            {
                await EnsureMonitorTenantAsync(command.CompanyId, ct);
                return Fulfilled(grant);
            }

            if (grant is null)
            {
                if (await companies.FindAsync(command.CompanyId, ct) is null) return LicensesErrors.CompanyNotFound;
                if ((await catalog.PlansAsync(ct)).All(p => p.Id != command.PlanId)) return LicensesErrors.PlanNotAvailable;
                grant = LicenseGrant.Start(command.CompanyId, command.PlanId, command.OrderReference, clock.GetUtcNow());
                store.Add(grant);
                await store.SaveAsync(ct);
            }

            var customerId = await PlatformCustomerAsync(command.CompanyId, ct);
            if (grant.PlatformSubscriptionId is null)
            {
                // INT-016: the reference makes a retry after a lost answer return the same subscription.
                grant.SubscriptionStarted(await platform.StartSubscriptionAsync(customerId, command.PlanId, SubscriptionReference(command.OrderReference), ct));
                await store.SaveAsync(ct);
            }

            // One worker at a time issues the license; another one retries later and then finds it issued.
            if (!grant.TryClaimIssue(clock.GetUtcNow())) return LicensesErrors.IssueInProgress;
            await store.SaveAsync(ct);

            PlatformIssuedLicense issued;
            try
            {
                issued = await IssueOnceAsync(customerId, grant.PlatformSubscriptionId!.Value, ct);
            }
            catch (PlatformUnavailableException)
            {
                // Even if LicensingPlatform did issue it, the next try finds the license on the subscription.
                grant.ReleaseIssueClaim();
                await store.SaveAsync(ct);
                throw;
            }

            grant.Issued(issued.License.Id, issued.License.LicenseNumber, issued.License.ExpiresAt, keys.Protect(issued.ProductKey), clock.GetUtcNow());
            await store.SaveAsync(ct);
            await EnsureMonitorTenantAsync(command.CompanyId, ct);
            return Fulfilled(grant);
        }
        catch (GrantConflictException)
        {
            // Another request created or claimed this order's grant first. It finishes the order; a retry reports it.
            return LicensesErrors.IssueInProgress;
        }
        catch (MonitorUnavailableException)
        {
            // The license steps that finished stay saved. The same order reference registers the tenant on the next try.
            return LicensesErrors.MonitorUnavailable;
        }
        catch (PlatformUnavailableException)
        {
            // Whatever finished is saved on the grant; calling again with the same order reference continues from there.
            return LicensesErrors.PlatformUnavailable;
        }
    }

    /// <summary>
    /// Each order starts its own subscription, so a license already on it was issued by an earlier try whose answer was
    /// lost. LicensingPlatform shows a product key only once, so that license gets a new key instead of a second license.
    /// </summary>
    private async Task<PlatformIssuedLicense> IssueOnceAsync(Guid customerId, Guid subscriptionId, CancellationToken ct)
    {
        var earlier = (await platform.LicensesOfAsync(customerId, ct)).FirstOrDefault(l => l.SubscriptionId == subscriptionId);
        return earlier is null
            ? await platform.IssueLicenseAsync(subscriptionId, ct)
            : await platform.RegenerateKeyAsync(earlier.Id, ct);
    }

    internal static string SubscriptionReference(string orderReference) => $"website-order:{orderReference}";

    internal static string CustomerReference(Guid companyId) => $"website-company:{companyId:N}";

    /// <summary>One Monitor Cloud tenant per company, linked to the Licensing customer this order already created.</summary>
    private async Task EnsureMonitorTenantAsync(Guid companyId, CancellationToken ct)
    {
        var customerId = await PlatformCustomerAsync(companyId, ct);
        var link = await store.LinkForAsync(companyId, ct) ?? throw new InvalidOperationException("Customer link disappeared.");
        if (link.MonitorTenantId is not null) return;
        var company = await companies.FindAsync(companyId, ct) ?? throw new InvalidOperationException("Company disappeared.");
        link.MonitorRegistered(await monitor.RegisterTenantAsync(customerId, company.Name, ct));
        await store.SaveAsync(ct);
    }

    private async Task<Guid> PlatformCustomerAsync(Guid companyId, CancellationToken ct)
    {
        if (await store.LinkForAsync(companyId, ct) is { } link) return link.PlatformCustomerId;
        var company = await companies.FindAsync(companyId, ct) ?? throw new InvalidOperationException("Company disappeared.");
        var owner = await users.FindAsync(company.OwnerUserId, ct);
        var customerId = await platform.CreateCustomerAsync(company.Name, owner?.Email ?? "", CustomerReference(companyId), ct);
        store.Add(new PlatformCustomerLink(companyId, customerId, clock.GetUtcNow()));
        await store.SaveAsync(ct);
        return customerId;
    }

    private static FulfilledLicense Fulfilled(LicenseGrant grant) => new(grant.Id, grant.LicenseNumber!, grant.ExpiresAt);
}

internal sealed class LicenseFulfillment(IDispatcher dispatcher) : ILicenseFulfillment
{
    public Task<Result<FulfilledLicense>> FulfillAsync(Guid companyId, Guid planId, string orderReference, CancellationToken ct) =>
        dispatcher.SendAsync(new FulfillLicenseCommand(companyId, planId, orderReference), ct);
}
