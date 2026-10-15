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
    ILicensingPlatform platform, ILicenseStore store, IKeyProtector keys, CatalogCache catalog,
    ICompanyDirectory companies, IUserDirectory users, TimeProvider clock)
    : ICommandHandler<FulfillLicenseCommand, Result<FulfilledLicense>>
{
    public async Task<Result<FulfilledLicense>> HandleAsync(FulfillLicenseCommand command, CancellationToken ct)
    {
        var grant = await store.GrantForOrderAsync(command.OrderReference, ct);
        if (grant is not null && !grant.IsFor(command.CompanyId, command.PlanId)) return LicensesErrors.OrderMismatch;
        if (grant is { Status: GrantStatus.Issued }) return Fulfilled(grant);

        try
        {
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
                grant.SubscriptionStarted(await platform.StartSubscriptionAsync(customerId, command.PlanId, ct));
                await store.SaveAsync(ct);
            }

            var issued = await platform.IssueLicenseAsync(grant.PlatformSubscriptionId!.Value, ct);
            grant.Issued(issued.License.Id, issued.License.LicenseNumber, issued.License.ExpiresAt, keys.Protect(issued.ProductKey), clock.GetUtcNow());
            await store.SaveAsync(ct);
            return Fulfilled(grant);
        }
        catch (PlatformUnavailableException)
        {
            // Whatever finished is saved on the grant; calling again with the same order reference continues from there.
            return LicensesErrors.PlatformUnavailable;
        }
    }

    private async Task<Guid> PlatformCustomerAsync(Guid companyId, CancellationToken ct)
    {
        if (await store.LinkForAsync(companyId, ct) is { } link) return link.PlatformCustomerId;
        var company = await companies.FindAsync(companyId, ct) ?? throw new InvalidOperationException("Company disappeared.");
        var owner = await users.FindAsync(company.OwnerUserId, ct);
        var customerId = await platform.CreateCustomerAsync(company.Name, owner?.Email ?? "", ct);
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
