using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Licenses.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Application;

public sealed record CatalogPlanDto(
    Guid Id, string ProductCode, string ProductName, string Code, string Name, decimal Price, string Currency,
    int? DurationDays, int? MaxDevices, IReadOnlyList<string> Features);

/// <summary>The plans on sale, for the public pricing page. Read from LicensingPlatform and kept for a few minutes.</summary>
public sealed record GetCatalogQuery : IQuery<Result<IReadOnlyList<CatalogPlanDto>>>;

/// <summary>Keeps the last catalog read so the public site doesn't call LicensingPlatform on every page view.</summary>
internal sealed class CatalogCache(ILicensingPlatform platform, TimeProvider clock) : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (IReadOnlyList<PlatformPlan> Plans, DateTimeOffset At)? _last;

    public async Task<IReadOnlyList<PlatformPlan>> PlansAsync(CancellationToken ct)
    {
        if (_last is { } fresh && clock.GetUtcNow() - fresh.At < Lifetime) return fresh.Plans;
        await _lock.WaitAsync(ct);
        try
        {
            if (_last is { } again && clock.GetUtcNow() - again.At < Lifetime) return again.Plans;
            try
            {
                var plans = await platform.PublishedPaidPlansAsync(ct);
                _last = (plans, clock.GetUtcNow());
                return plans;
            }
            // A stale price list beats an empty pricing page while LicensingPlatform is down.
            catch (PlatformUnavailableException) when (_last is { } stale)
            {
                return stale.Plans;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();
}

internal sealed class GetCatalogHandler(CatalogCache catalog) : IQueryHandler<GetCatalogQuery, Result<IReadOnlyList<CatalogPlanDto>>>
{
    public async Task<Result<IReadOnlyList<CatalogPlanDto>>> HandleAsync(GetCatalogQuery query, CancellationToken ct)
    {
        try
        {
            var plans = await catalog.PlansAsync(ct);
            return plans.Select(p => new CatalogPlanDto(p.Id, p.ProductCode, p.ProductName, p.Code, p.Name, p.Price, p.Currency,
                p.DurationDays, p.MaxActivations, p.Features)).ToList();
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }
}
