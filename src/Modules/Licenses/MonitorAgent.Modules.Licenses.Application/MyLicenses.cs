using Microsoft.Extensions.Logging;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Licenses.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Application;

public sealed record LicenseSummaryDto(
    Guid Id, string LicenseNumber, string ProductCode, string PlanCode, string ProductKeyPrefix, string Status,
    DateTimeOffset IssuedAt, DateTimeOffset? ExpiresAt, int? MaxDevices, int ActiveDevices);

public sealed record DeviceDto(
    Guid Id, string DeviceId, string? DeviceName, string? OperatingSystem, string? AppVersion, string Status,
    DateTimeOffset ActivatedAt, DateTimeOffset? LastSeenAt, bool Online);

public sealed record LicenseDetailsDto(LicenseSummaryDto License, IReadOnlyList<DeviceDto> Devices);
public sealed record ProductKeyDto(string ProductKey);

/// <summary>The caller's company licenses. Every query is scoped to that company's LicensingPlatform customer.</summary>
public sealed record ListMyLicensesQuery : IQuery<Result<IReadOnlyList<LicenseSummaryDto>>>;
public sealed record GetMyLicenseQuery(Guid LicenseId) : IQuery<Result<LicenseDetailsDto>>;

/// <summary>Shows the full product key again. Owners and technical members only, and every reveal is logged.</summary>
public sealed record RevealProductKeyCommand(Guid LicenseId) : ICommand<Result<ProductKeyDto>>;

/// <summary>Frees a device's seat on the license, for a replaced or reinstalled machine.</summary>
public sealed record ResetDeviceCommand(Guid LicenseId, Guid DeviceId) : ICommand<Result>;

/// <summary>Resolves the caller's company and its LicensingPlatform customer, the only tenant these handlers may read.</summary>
internal sealed class CompanyScope(ICurrentUser me, ICompanyDirectory companies, ILicenseStore store)
{
    public async Task<(CompanyMembership Membership, Guid? PlatformCustomerId)?> ResolveAsync(CancellationToken ct)
    {
        if (me.UserId is not { } userId || await companies.MembershipOfAsync(userId, ct) is not { } membership) return null;
        var link = await store.LinkForAsync(membership.CompanyId, ct);
        return (membership, link?.PlatformCustomerId);
    }

    /// <summary>The license, only if it belongs to the caller's company.</summary>
    public static async Task<(PlatformLicense License, IReadOnlyList<PlatformDevice> Devices)?> OwnLicenseAsync(
        ILicensingPlatform platform, Guid? customerId, Guid licenseId, CancellationToken ct)
    {
        if (customerId is null) return null;
        var found = await platform.LicenseAsync(licenseId, ct);
        return found is { } f && f.License.CustomerId == customerId ? f : null;
    }
}

internal static class Map
{
    public static LicenseSummaryDto Summary(PlatformLicense l) => new(l.Id, l.LicenseNumber, l.ProductCode, l.PlanCode, l.ProductKeyPrefix,
        l.Status, l.IssuedAt, l.ExpiresAt, l.MaxActivations, l.ActiveActivations);

    public static DeviceDto Device(PlatformDevice d) => new(d.Id, d.DeviceId, d.DeviceName, d.OperatingSystem, d.AppVersion, d.Status,
        d.ActivatedAt, d.LastHeartbeatAt, d.Online);
}

internal sealed class ListMyLicensesHandler(CompanyScope scope, ILicensingPlatform platform)
    : IQueryHandler<ListMyLicensesQuery, Result<IReadOnlyList<LicenseSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<LicenseSummaryDto>>> HandleAsync(ListMyLicensesQuery query, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (tenant.PlatformCustomerId is not { } customerId) return Array.Empty<LicenseSummaryDto>();
        try
        {
            return (await platform.LicensesOfAsync(customerId, ct)).Where(l => l.CustomerId == customerId).Select(Map.Summary).ToList();
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }
}

internal sealed class GetMyLicenseHandler(CompanyScope scope, ILicensingPlatform platform) : IQueryHandler<GetMyLicenseQuery, Result<LicenseDetailsDto>>
{
    public async Task<Result<LicenseDetailsDto>> HandleAsync(GetMyLicenseQuery query, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        try
        {
            if (await CompanyScope.OwnLicenseAsync(platform, tenant.PlatformCustomerId, query.LicenseId, ct) is not { } found)
                return LicensesErrors.LicenseNotFound;
            return new LicenseDetailsDto(Map.Summary(found.License), found.Devices.Select(Map.Device).ToList());
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }
}

internal sealed partial class RevealProductKeyHandler(CompanyScope scope, ILicenseStore store, IKeyProtector keys, ICurrentUser me, ILogger<RevealProductKeyHandler> logger)
    : ICommandHandler<RevealProductKeyCommand, Result<ProductKeyDto>>
{
    public async Task<Result<ProductKeyDto>> HandleAsync(RevealProductKeyCommand command, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (!tenant.Membership.CanManageDevices) return LicensesErrors.NotAllowed;
        var grant = await store.GrantForLicenseAsync(tenant.Membership.CompanyId, command.LicenseId, ct);
        if (grant?.ProtectedProductKey is null) return LicensesErrors.KeyNotAvailable;

        KeyRevealed(logger, grant.Id, me.UserId!.Value);
        return new ProductKeyDto(keys.Unprotect(grant.ProtectedProductKey));
    }

    [LoggerMessage(LogLevel.Information, "Product key of grant {GrantId} revealed to user {UserId}")]
    private static partial void KeyRevealed(ILogger logger, Guid grantId, Guid userId);
}

internal sealed class ResetDeviceHandler(CompanyScope scope, ILicensingPlatform platform) : ICommandHandler<ResetDeviceCommand, Result>
{
    public async Task<Result> HandleAsync(ResetDeviceCommand command, CancellationToken ct)
    {
        if (await scope.ResolveAsync(ct) is not { } tenant) return LicensesErrors.NoCompany;
        if (!tenant.Membership.CanManageDevices) return LicensesErrors.NotAllowed;
        try
        {
            if (await CompanyScope.OwnLicenseAsync(platform, tenant.PlatformCustomerId, command.LicenseId, ct) is not { } found)
                return LicensesErrors.LicenseNotFound;
            if (found.Devices.All(d => d.Id != command.DeviceId)) return LicensesErrors.DeviceNotFound;
            await platform.ResetDeviceAsync(command.LicenseId, command.DeviceId, ct);
            return Result.Success();
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }
}
