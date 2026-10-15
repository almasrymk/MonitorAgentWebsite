using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.BuildingBlocks.Application;

/// <summary>
/// A command worth a line in the audit trail: who did what to which thing. The pipeline records it after the handler
/// succeeds, so a failed or rejected attempt is never written as if it had happened.
/// </summary>
public interface IAuditedCommand
{
    /// <summary>What happened, as module.verb, e.g. "order.manual-payment".</summary>
    string AuditAction { get; }

    /// <summary>What it happened to, as kind:id, e.g. "order:0199...".</summary>
    string AuditTarget { get; }

    /// <summary>A short why or reference, when the command carries one. Never secrets or personal data beyond an email.</summary>
    string? AuditDetail => null;
}

public sealed record AuditRecord(string Action, string Target, Guid? ActorUserId, string? Detail);

/// <summary>Where audit records go. Implemented by the Audit module.</summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditRecord record, CancellationToken ct);
}

/// <summary>
/// Writes an <see cref="IAuditedCommand"/> to the audit trail once it has succeeded. The change is already saved by then,
/// so a failure to write the audit line is logged as an error rather than reported to the caller as a failed request.
/// </summary>
internal sealed partial class AuditBehavior<TRequest, TResult>(IServiceProvider services, ILogger<AuditBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult> where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct)
    {
        var result = await next();
        if (request is not IAuditedCommand audited || result is Result { IsFailure: true }) return result;
        if (services.GetService<IAuditTrail>() is not { } trail) return result;
        try
        {
            await trail.RecordAsync(new AuditRecord(audited.AuditAction, audited.AuditTarget, services.GetService<ICurrentUser>()?.UserId, audited.AuditDetail), CancellationToken.None);
        }
        catch (Exception ex)
        {
            AuditFailed(logger, ex, audited.AuditAction, audited.AuditTarget);
        }
        return result;
    }

    [LoggerMessage(LogLevel.Error, "Could not write the audit record for {Action} on {Target}")]
    private static partial void AuditFailed(ILogger logger, Exception ex, string action, string target);
}
