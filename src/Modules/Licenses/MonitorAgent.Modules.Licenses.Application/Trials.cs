using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Licenses.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Application;

/// <summary>The product a website trial code is for.</summary>
public sealed record TrialOptions(string ProductCode);

/// <summary>The trial code to type into the Agent's activation screen, shown once.</summary>
public sealed record TrialCodeDto(string TrialCode, DateTimeOffset ExpiresAt, string ProductCode, int TrialDays);

/// <summary>
/// B5 (product owner 2026-11-08, Licensing ADR-016): a signed-in customer asks for a trial of the Agent. The website asks
/// LicensingPlatform for a single-use code tied to this account; the Agent redeems it on the computer to be monitored, with
/// no shared secret in the installer. One trial per account (LicensingPlatform enforces it) and one per computer.
/// </summary>
public sealed record StartMyTrialCommand : ICommand<Result<TrialCodeDto>>, IAuditedCommand
{
    public string AuditAction => "license.trial-code";
    public string AuditTarget => "trial";
}

internal sealed class StartMyTrialHandler(ICurrentUser me, ILicensingPlatform platform, TrialOptions options) : ICommandHandler<StartMyTrialCommand, Result<TrialCodeDto>>
{
    public static string Reference(Guid userId) => $"website-user:{userId:N}";

    public async Task<Result<TrialCodeDto>> HandleAsync(StartMyTrialCommand command, CancellationToken ct)
    {
        if (me.UserId is not { } userId) return LicensesErrors.NotAllowed;
        try
        {
            var grant = await platform.IssueTrialGrantAsync(options.ProductCode, Reference(userId), ct);
            return new TrialCodeDto(grant.TrialCode, grant.ExpiresAt, grant.ProductCode, grant.TrialDays);
        }
        catch (PlatformRejectedException ex) when (ex.Code == "LIC_TRIAL_GRANT_USED")
        {
            return LicensesErrors.TrialAlreadyUsed;
        }
        catch (PlatformRejectedException ex) when (ex.Code == "LIC_TRIAL_UNAVAILABLE")
        {
            return LicensesErrors.TrialUnavailable;
        }
        catch (PlatformUnavailableException)
        {
            return LicensesErrors.PlatformUnavailable;
        }
    }
}
