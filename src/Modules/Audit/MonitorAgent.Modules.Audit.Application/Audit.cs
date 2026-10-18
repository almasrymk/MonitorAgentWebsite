using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Audit.Application;

/// <summary>One line of the audit trail. Append-only: nothing in the application changes or deletes one.</summary>
public sealed class AuditEntry : Entity
{
    public const int MaxAction = 60;
    public const int MaxTarget = 100;
    public const int MaxDetail = 500;

    private AuditEntry() { }

    public DateTimeOffset At { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = "";
    public string Target { get; private set; } = "";
    public string? Detail { get; private set; }
    public string? IpAddress { get; private set; }

    public static AuditEntry From(AuditRecord record, DateTimeOffset at, string? ip) => new()
    {
        At = at,
        ActorUserId = record.ActorUserId,
        Action = Cut(record.Action, MaxAction)!,
        Target = Cut(record.Target, MaxTarget)!,
        Detail = Cut(record.Detail, MaxDetail),
        IpAddress = ip,
    };

    private static string? Cut(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}

internal interface IAuditStore
{
    Task<PagedResult<AuditEntry>> ListAsync(string? action, Guid? actor, string? target, int page, int pageSize, CancellationToken ct);
}

public sealed record AuditEntryDto(Guid Id, DateTimeOffset At, Guid? ActorUserId, string? ActorName, string? ActorEmail, string Action, string Target,
    string? Detail, string? IpAddress);

/// <summary>Newest first. Filter by action prefix ("content." for every content step), by who did it, or by the exact target.</summary>
public sealed record ListAuditQuery(string? Action, Guid? ActorUserId, string? Target, int Page = 1, int PageSize = 50) : IQuery<Result<PagedResult<AuditEntryDto>>>;

internal sealed class ListAuditValidator : AbstractValidator<ListAuditQuery>
{
    public ListAuditValidator()
    {
        RuleFor(x => x.Action).MaximumLength(AuditEntry.MaxAction);
        RuleFor(x => x.Target).MaximumLength(AuditEntry.MaxTarget);
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class ListAuditHandler(IAuditStore store, IUserDirectory users) : IQueryHandler<ListAuditQuery, Result<PagedResult<AuditEntryDto>>>
{
    public async Task<Result<PagedResult<AuditEntryDto>>> HandleAsync(ListAuditQuery query, CancellationToken ct)
    {
        var page = await store.ListAsync(query.Action?.Trim(), query.ActorUserId, query.Target?.Trim(), query.Page, query.PageSize, ct);
        var people = await users.FindManyAsync(page.Items.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value).Distinct().ToList(), ct);
        var items = page.Items.Select(e =>
        {
            var actor = e.ActorUserId is { } id ? people.GetValueOrDefault(id) : null;
            return new AuditEntryDto(e.Id, e.At, e.ActorUserId, actor?.FullName, actor?.Email, e.Action, e.Target, e.Detail, e.IpAddress);
        }).ToList();
        return new PagedResult<AuditEntryDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddAuditApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddHandlersFrom(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
