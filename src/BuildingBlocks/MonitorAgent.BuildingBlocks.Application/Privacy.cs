using MonitorAgent.SharedKernel;

namespace MonitorAgent.BuildingBlocks.Application;

/// <summary>
/// One module's share of what the website holds about a person. Identity asks every registered source when someone
/// downloads their data or deletes their account, so a new module that stores something about people adds one of these
/// and nothing else has to change.
/// </summary>
public interface IPersonalDataSource
{
    /// <summary>The section name in the export, such as "orders".</summary>
    string Section { get; }

    /// <summary>What this module holds about the person, ready to serialize, or null when it holds nothing.</summary>
    Task<object?> ExportAsync(Guid userId, CancellationToken ct);

    /// <summary>A reason the account can't be deleted yet (the person still has to do something first).</summary>
    Task<Result> CanEraseAsync(Guid userId, CancellationToken ct) => Task.FromResult(Result.Success());

    /// <summary>
    /// Removes or anonymizes what this module holds about the person. Records the law requires to be kept (invoices,
    /// payments) stay, linked only by the account id that no longer leads to a name or email.
    /// </summary>
    Task EraseAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
}
