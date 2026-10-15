using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Customers.Domain;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Customers.Application;

/// <summary>
/// The staff read side: every company, by id or by search. Separate from <see cref="ICustomerAccounts"/> on purpose, so
/// customer handlers still have no way to address a company other than their own. Only staff endpoints use it.
/// </summary>
internal interface IStaffCompanyReader
{
    Task<(IReadOnlyList<CustomerAccount> Items, int Total)> SearchAsync(string? name, Guid? memberUserId, int page, int pageSize, CancellationToken ct);
    Task<CustomerAccount?> FindAsync(Guid companyId, CancellationToken ct);
}

public sealed record CompanyListItemDto(Guid Id, string Name, string? OwnerName, string? OwnerEmail, int Members, DateTimeOffset CreatedAt);
public sealed record CompanyMemberDto(Guid UserId, string FullName, string Email, MemberRole Role, DateTimeOffset JoinedAt);
public sealed record CompanyInvitationDto(string Email, MemberRole Role, DateTimeOffset ExpiresAt);
public sealed record CompanyDetailsDto(Guid Id, string Name, DateTimeOffset CreatedAt, IReadOnlyList<CompanyMemberDto> Members,
    IReadOnlyList<CompanyInvitationDto> PendingInvitations);

/// <summary>Companies whose name contains the text, or the company of the person with that exact email.</summary>
public sealed record SearchCompaniesQuery(string? Search, int Page = 1, int PageSize = 25) : IQuery<Result<PagedResult<CompanyListItemDto>>>;
public sealed record GetCompanyForStaffQuery(Guid CompanyId) : IQuery<Result<CompanyDetailsDto>>;

internal sealed class SearchCompaniesValidator : AbstractValidator<SearchCompaniesQuery>
{
    public SearchCompaniesValidator()
    {
        RuleFor(x => x.Search).MaximumLength(150);
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class SearchCompaniesHandler(IStaffCompanyReader reader, IUserDirectory users)
    : IQueryHandler<SearchCompaniesQuery, Result<PagedResult<CompanyListItemDto>>>
{
    public async Task<Result<PagedResult<CompanyListItemDto>>> HandleAsync(SearchCompaniesQuery query, CancellationToken ct)
    {
        var text = query.Search?.Trim();
        string? name = string.IsNullOrEmpty(text) ? null : text;
        Guid? member = null;
        if (text is not null && text.Contains('@', StringComparison.Ordinal))
        {
            // Nobody with that email: an empty page, not a name search for "x@y".
            member = (await users.FindByEmailAsync(text, ct))?.Id ?? Guid.Empty;
            name = null;
        }
        var (items, total) = await reader.SearchAsync(name, member, query.Page, query.PageSize, ct);
        var owners = await users.FindManyAsync(items.Select(Owner).Distinct().ToList(), ct);
        var rows = items.Select(a =>
        {
            var owner = owners.GetValueOrDefault(Owner(a));
            return new CompanyListItemDto(a.Id, a.Name, owner?.FullName, owner?.Email, a.Members.Count, a.CreatedAt);
        }).ToList();
        return new PagedResult<CompanyListItemDto>(rows, query.Page, query.PageSize, total);
    }

    private static Guid Owner(CustomerAccount a) => a.Members.Where(m => m.Role == MemberRole.Owner).OrderBy(m => m.JoinedAt).First().UserId;
}

internal sealed class GetCompanyForStaffHandler(IStaffCompanyReader reader, IUserDirectory users, TimeProvider clock)
    : IQueryHandler<GetCompanyForStaffQuery, Result<CompanyDetailsDto>>
{
    public async Task<Result<CompanyDetailsDto>> HandleAsync(GetCompanyForStaffQuery query, CancellationToken ct)
    {
        if (await reader.FindAsync(query.CompanyId, ct) is not { } account) return CustomersErrors.CompanyNotFound;
        var people = await users.FindManyAsync(account.Members.Select(m => m.UserId).ToList(), ct);
        var members = account.Members.OrderBy(m => m.Role).ThenBy(m => m.JoinedAt).Select(m =>
        {
            var person = people.GetValueOrDefault(m.UserId);
            return new CompanyMemberDto(m.UserId, person?.FullName ?? "—", person?.Email ?? "—", m.Role, m.JoinedAt);
        }).ToList();
        var now = clock.GetUtcNow();
        var invitations = account.Invitations.Where(i => i.IsOpen(now))
            .Select(i => new CompanyInvitationDto(i.Email, i.Role, i.ExpiresAt)).ToList();
        return new CompanyDetailsDto(account.Id, account.Name, account.CreatedAt, members, invitations);
    }
}
