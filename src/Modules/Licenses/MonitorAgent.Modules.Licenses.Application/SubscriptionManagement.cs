using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Licenses.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Application;

public sealed record CompanySubscriptionDto(Guid Id, Guid PlanId, string PlanCode, string PlanName, string Status, DateTimeOffset StartDate, DateTimeOffset? EndDate);
public sealed record IssuedDeviceLicenseDto(Guid LicenseId, string LicenseNumber, string ProductKey);
public sealed record ChangePlanRequest(Guid PlanId);

public sealed record ListMySubscriptionsQuery : IQuery<Result<IReadOnlyList<CompanySubscriptionDto>>>;

public sealed record ChangeMyPlanCommand(Guid SubscriptionId, Guid PlanId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "subscription.change-plan";
    public string AuditTarget => $"subscription:{SubscriptionId}";
    public string? AuditDetail => $"plan {PlanId}";
}

public sealed record SuspendMySubscriptionCommand(Guid SubscriptionId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "subscription.suspend";
    public string AuditTarget => $"subscription:{SubscriptionId}";
}

public sealed record ResumeMySubscriptionCommand(Guid SubscriptionId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "subscription.resume";
    public string AuditTarget => $"subscription:{SubscriptionId}";
}

public sealed record CancelMySubscriptionCommand(Guid SubscriptionId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "subscription.cancel";
    public string AuditTarget => $"subscription:{SubscriptionId}";
}

public sealed record IssueMyLicenseCommand(Guid SubscriptionId) : ICommand<Result<IssuedDeviceLicenseDto>>, IAuditedCommand
{
    public string AuditAction => "license.issue-extra";
    public string AuditTarget => $"subscription:{SubscriptionId}";
}

public sealed record RegenerateMyKeyCommand(Guid LicenseId) : ICommand<Result<ProductKeyDto>>, IAuditedCommand
{
    public string AuditAction => "license.regenerate-key";
    public string AuditTarget => $"license:{LicenseId}";
}

public sealed record SuspendMyLicenseCommand(Guid LicenseId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "license.suspend";
    public string AuditTarget => $"license:{LicenseId}";
}

public sealed record ResumeMyLicenseCommand(Guid LicenseId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "license.resume";
    public string AuditTarget => $"license:{LicenseId}";
}

public sealed record RevokeMyLicenseCommand(Guid LicenseId) : ICommand<Result>, IAuditedCommand
{
    public string AuditAction => "license.revoke";
    public string AuditTarget => $"license:{LicenseId}";
}

internal static class LicensingCall
{
    public static async Task<Result> Run(Func<Task> call)
    {
        try
        {
            await call();
            return Result.Success();
        }
        catch (Exception ex) when (Map(ex) is not null)
        {
            return Map(ex)!;
        }
    }

    public static async Task<Result<T>> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (Map(ex) is not null)
        {
            return Map(ex)!;
        }
    }

    private static Error? Map(Exception ex) => ex switch
    {
        PlatformUnavailableException => LicensesErrors.PlatformUnavailable,
        PlatformRejectedException rejected => rejected.Status switch
        {
            404 => Error.NotFound(rejected.Code, rejected.Message),
            403 => Error.Forbidden(rejected.Code, rejected.Message),
            409 => Error.Conflict(rejected.Code, rejected.Message),
            _ => Error.Validation(rejected.Code, rejected.Message),
        },
        _ => null,
    };
}

internal sealed class ListMySubscriptionsHandler(CompanyScope scope, ILicensingPlatform platform)
    : IQueryHandler<ListMySubscriptionsQuery, Result<IReadOnlyList<CompanySubscriptionDto>>>
{
    public async Task<Result<IReadOnlyList<CompanySubscriptionDto>>> HandleAsync(ListMySubscriptionsQuery query, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (tenant.PlatformCustomerId is not { } customerId) return Array.Empty<CompanySubscriptionDto>();
        return await LicensingCall.Run<IReadOnlyList<CompanySubscriptionDto>>(async () =>
            (await platform.SubscriptionsOfAsync(customerId, ct)).Where(s => s.CustomerId == customerId)
                .Select(s => new CompanySubscriptionDto(s.Id, s.PlanId, s.PlanCode, s.PlanName, s.Status, s.StartDate, s.EndDate)).ToList());
    }
}

internal sealed class ChangeMyPlanHandler(CompanyScope scope, ILicensingPlatform platform) : ICommandHandler<ChangeMyPlanCommand, Result>
{
    public async Task<Result> HandleAsync(ChangeMyPlanCommand command, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (!tenant.Membership.CanManageBilling) return LicensesErrors.NotAllowed;
        try
        {
            if (await OwnAsync(platform, tenant.PlatformCustomerId, command.SubscriptionId, ct) is null) return LicensesErrors.SubscriptionNotFound;
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
        var plans = await LicensingCall.Run(() => platform.PublishedPaidPlansAsync(ct));
        if (plans.IsFailure) return plans.Error!;
        if (plans.Value.All(p => p.Id != command.PlanId)) return LicensesErrors.PlanNotAvailable;
        return await LicensingCall.Run(() => platform.ChangePlanAsync(command.SubscriptionId, command.PlanId, ct));
    }

    internal static async Task<PlatformSubscription?> OwnAsync(ILicensingPlatform platform, Guid? customerId, Guid subscriptionId, CancellationToken ct)
    {
        if (customerId is null) return null;
        try
        {
            return (await platform.SubscriptionsOfAsync(customerId.Value, ct)).FirstOrDefault(s => s.Id == subscriptionId && s.CustomerId == customerId);
        }
        catch (PlatformUnavailableException)
        {
            throw;
        }
    }
}

internal sealed class SuspendMySubscriptionHandler(CompanyScope scope, ILicensingPlatform platform) : ICommandHandler<SuspendMySubscriptionCommand, Result>
{
    public async Task<Result> HandleAsync(SuspendMySubscriptionCommand command, CancellationToken ct)
    {
        if (await BillingAsync(scope, platform, command.SubscriptionId, ct) is { } error) return error;
        return await LicensingCall.Run(() => platform.SuspendSubscriptionAsync(command.SubscriptionId, null, ct));
    }

    internal static async Task<Error?> BillingAsync(CompanyScope scope, ILicensingPlatform platform, Guid subscriptionId, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (!tenant.Membership.CanManageBilling) return LicensesErrors.NotAllowed;
        try
        {
            return await ChangeMyPlanHandler.OwnAsync(platform, tenant.PlatformCustomerId, subscriptionId, ct) is null ? LicensesErrors.SubscriptionNotFound : null;
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }
}

internal sealed class ResumeMySubscriptionHandler(CompanyScope scope, ILicensingPlatform platform) : ICommandHandler<ResumeMySubscriptionCommand, Result>
{
    public async Task<Result> HandleAsync(ResumeMySubscriptionCommand command, CancellationToken ct)
    {
        if (await SuspendMySubscriptionHandler.BillingAsync(scope, platform, command.SubscriptionId, ct) is { } error) return error;
        return await LicensingCall.Run(() => platform.ResumeSubscriptionAsync(command.SubscriptionId, ct));
    }
}

internal sealed class CancelMySubscriptionHandler(CompanyScope scope, ILicensingPlatform platform) : ICommandHandler<CancelMySubscriptionCommand, Result>
{
    public async Task<Result> HandleAsync(CancelMySubscriptionCommand command, CancellationToken ct)
    {
        if (await SuspendMySubscriptionHandler.BillingAsync(scope, platform, command.SubscriptionId, ct) is { } error) return error;
        return await LicensingCall.Run(() => platform.CancelSubscriptionAsync(command.SubscriptionId, null, ct));
    }
}

internal sealed class IssueMyLicenseHandler(CompanyScope scope, ILicensingPlatform platform, ILicenseStore store, IKeyProtector keys, TimeProvider clock)
    : ICommandHandler<IssueMyLicenseCommand, Result<IssuedDeviceLicenseDto>>
{
    public async Task<Result<IssuedDeviceLicenseDto>> HandleAsync(IssueMyLicenseCommand command, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (!tenant.Membership.CanManageDevices) return LicensesErrors.NotAllowed;
        PlatformSubscription? subscription;
        try
        {
            subscription = await ChangeMyPlanHandler.OwnAsync(platform, tenant.PlatformCustomerId, command.SubscriptionId, ct);
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
        if (subscription is null) return LicensesErrors.SubscriptionNotFound;
        var issued = await LicensingCall.Run(() => platform.IssueLicenseAsync(command.SubscriptionId, ct));
        if (issued.IsFailure) return issued.Error!;
        var grant = LicenseGrant.Start(tenant.Membership.CompanyId, subscription.PlanId, $"issued-{Guid.NewGuid():N}", clock.GetUtcNow());
        grant.SubscriptionStarted(command.SubscriptionId);
        grant.Issued(issued.Value.License.Id, issued.Value.License.LicenseNumber, issued.Value.License.ExpiresAt, keys.Protect(issued.Value.ProductKey), clock.GetUtcNow());
        store.Add(grant);
        await store.SaveAsync(ct);
        return new IssuedDeviceLicenseDto(issued.Value.License.Id, issued.Value.License.LicenseNumber, issued.Value.ProductKey);
    }
}

internal sealed class RegenerateMyKeyHandler(CompanyScope scope, ILicensingPlatform platform, ILicenseStore store, IKeyProtector keys)
    : ICommandHandler<RegenerateMyKeyCommand, Result<ProductKeyDto>>
{
    public async Task<Result<ProductKeyDto>> HandleAsync(RegenerateMyKeyCommand command, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (!tenant.Membership.CanManageDevices) return LicensesErrors.NotAllowed;
        try
        {
            if (await CompanyScope.OwnLicenseAsync(platform, tenant.PlatformCustomerId, command.LicenseId, ct) is null)
                return LicensesErrors.LicenseNotFound;
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
        var grant = await store.GrantForLicenseAsync(tenant.Membership.CompanyId, command.LicenseId, ct);
        if (grant?.ProtectedProductKey is null) return LicensesErrors.KeyNotAvailable;
        var issued = await LicensingCall.Run(() => platform.RegenerateKeyAsync(command.LicenseId, ct));
        if (issued.IsFailure) return issued.Error!;
        grant.ReplaceKey(keys.Protect(issued.Value.ProductKey));
        await store.SaveAsync(ct);
        return new ProductKeyDto(issued.Value.ProductKey);
    }
}

internal sealed class SuspendMyLicenseHandler(CompanyScope scope, ILicensingPlatform platform) : ICommandHandler<SuspendMyLicenseCommand, Result>
{
    public async Task<Result> HandleAsync(SuspendMyLicenseCommand command, CancellationToken ct)
    {
        if (await DevicesAsync(scope, platform, command.LicenseId, ct) is { } error) return error;
        return await LicensingCall.Run(() => platform.SuspendLicenseAsync(command.LicenseId, null, ct));
    }

    internal static async Task<Error?> DevicesAsync(CompanyScope scope, ILicensingPlatform platform, Guid licenseId, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (!tenant.Membership.CanManageDevices) return LicensesErrors.NotAllowed;
        try
        {
            return await CompanyScope.OwnLicenseAsync(platform, tenant.PlatformCustomerId, licenseId, ct) is null ? LicensesErrors.LicenseNotFound : null;
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }
}

internal sealed class ResumeMyLicenseHandler(CompanyScope scope, ILicensingPlatform platform) : ICommandHandler<ResumeMyLicenseCommand, Result>
{
    public async Task<Result> HandleAsync(ResumeMyLicenseCommand command, CancellationToken ct)
    {
        if (await SuspendMyLicenseHandler.DevicesAsync(scope, platform, command.LicenseId, ct) is { } error) return error;
        return await LicensingCall.Run(() => platform.ResumeLicenseAsync(command.LicenseId, ct));
    }
}

internal sealed class RevokeMyLicenseHandler(CompanyScope scope, ILicensingPlatform platform) : ICommandHandler<RevokeMyLicenseCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeMyLicenseCommand command, CancellationToken ct)
    {
        if (await SuspendMyLicenseHandler.DevicesAsync(scope, platform, command.LicenseId, ct) is { } error) return error;
        return await LicensingCall.Run(() => platform.RevokeLicenseAsync(command.LicenseId, null, ct));
    }
}
