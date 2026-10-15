using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Application;

public sealed record MeDto(Guid Id, string Email, string FullName, bool EmailConfirmed, bool MfaEnabled, IReadOnlyList<string> Roles);

public sealed record GetMeQuery : IQuery<Result<MeDto>>;

/// <summary>Admin view of the security log, newest first, optionally for one account.</summary>
public sealed record ListSecurityEventsQuery(Guid? UserId, int Page = 1, int PageSize = 50) : IQuery<PagedResult<SecurityEventDto>>;

internal sealed class ListSecurityEventsValidator : AbstractValidator<ListSecurityEventsQuery>
{
    public ListSecurityEventsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class GetMeHandler(IUserAccounts users, ICurrentUser me) : IQueryHandler<GetMeQuery, Result<MeDto>>
{
    public async Task<Result<MeDto>> HandleAsync(GetMeQuery query, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await users.FindByIdAsync(userId) is not { } user) return IdentityErrors.Unauthenticated;
        return new MeDto(user.Id, user.Email, user.FullName, user.EmailConfirmed, user.MfaEnabled, user.Roles);
    }
}

internal sealed class ListSecurityEventsHandler(ISecurityEventQueries events) : IQueryHandler<ListSecurityEventsQuery, PagedResult<SecurityEventDto>>
{
    public Task<PagedResult<SecurityEventDto>> HandleAsync(ListSecurityEventsQuery query, CancellationToken ct) =>
        events.ListAsync(query.UserId, query.Page, query.PageSize, ct);
}
