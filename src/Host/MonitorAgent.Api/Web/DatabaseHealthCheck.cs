using Microsoft.Extensions.Diagnostics.HealthChecks;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;

namespace MonitorAgent.Api.Web;

/// <summary>
/// Ready means the database answers. Every module shares the one connection string, so one context is enough. Live has
/// no checks, so a database outage takes the instance out of the load balancer without the orchestrator restarting it.
/// </summary>
internal sealed class DatabaseHealthCheck(IdentityDb db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The database does not answer.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database does not answer.", ex);
        }
    }
}
