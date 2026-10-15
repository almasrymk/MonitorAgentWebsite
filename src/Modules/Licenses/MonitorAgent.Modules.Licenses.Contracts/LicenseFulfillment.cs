using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Contracts;

public sealed record FulfilledLicense(Guid GrantId, string LicenseNumber, DateTimeOffset? ExpiresAt);

/// <summary>
/// Issues the license a paid order bought. Safe to call again with the same <paramref name="orderReference"/>
/// (a retried payment callback): it resumes or returns the license already issued, never a second one.
/// </summary>
public interface ILicenseFulfillment
{
    Task<Result<FulfilledLicense>> FulfillAsync(Guid companyId, Guid planId, string orderReference, CancellationToken ct);
}
