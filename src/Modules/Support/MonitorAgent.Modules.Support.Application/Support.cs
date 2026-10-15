using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Support.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Support.Application;

public interface ISupportStore
{
    Task<Ticket?> TicketAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Ticket>> CompanyTicketsAsync(Guid companyId, CancellationToken ct);
    Task<int> OpenTicketCountAsync(Guid companyId, CancellationToken ct);
    Task<PagedResult<Ticket>> TicketsAsync(TicketStatus? status, int page, int pageSize, CancellationToken ct);
    Task<Suggestion?> SuggestionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Suggestion>> CompanySuggestionsAsync(Guid companyId, CancellationToken ct);
    Task<PagedResult<Suggestion>> SuggestionsAsync(SuggestionStatus? status, int page, int pageSize, CancellationToken ct);
    void Add(Ticket ticket);
    void Add(Suggestion suggestion);
    Task SaveAsync(CancellationToken ct);
}

public sealed record TicketSummaryDto(Guid Id, string Number, string Subject, TicketCategory Category, TicketStatus Status, DateTimeOffset OpenedAt,
    DateTimeOffset LastActivityAt, string? CompanyName);
public sealed record TicketMessageDto(Guid Id, string AuthorName, bool FromStaff, string Body, DateTimeOffset PostedAt);
public sealed record TicketDto(Guid Id, string Number, string Subject, TicketCategory Category, TicketStatus Status, DateTimeOffset OpenedAt,
    string? CompanyName, IReadOnlyList<TicketMessageDto> Messages);
public sealed record SuggestionDto(Guid Id, string Title, string Body, SuggestionStatus Status, string? StaffNote, DateTimeOffset CreatedAt,
    string? CompanyName);

public sealed record OpenTicketCommand(string Subject, TicketCategory Category, string Body) : ICommand<Result<TicketDto>>;
public sealed record ReplyToTicketCommand(Guid TicketId, string Body, bool AsStaff) : ICommand<Result<TicketDto>>;
public sealed record CloseTicketCommand(Guid TicketId, bool AsStaff) : ICommand<Result<TicketDto>>;
public sealed record MyTicketsQuery : IQuery<Result<IReadOnlyList<TicketSummaryDto>>>;
public sealed record GetTicketQuery(Guid TicketId, bool AsStaff) : IQuery<Result<TicketDto>>;
public sealed record ListTicketsQuery(TicketStatus? Status, int Page, int PageSize) : IQuery<Result<PagedResult<TicketSummaryDto>>>;
public sealed record SubmitSuggestionCommand(string Title, string Body) : ICommand<Result<SuggestionDto>>;
public sealed record MySuggestionsQuery : IQuery<Result<IReadOnlyList<SuggestionDto>>>;
public sealed record ListSuggestionsQuery(SuggestionStatus? Status, int Page, int PageSize) : IQuery<Result<PagedResult<SuggestionDto>>>;
public sealed record AnswerSuggestionCommand(Guid SuggestionId, SuggestionStatus Status, string? Note) : ICommand<Result<SuggestionDto>>;

internal sealed class OpenTicketValidator : AbstractValidator<OpenTicketCommand>
{
    public OpenTicketValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(Ticket.MaxSubject);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Ticket.MaxBody);
    }
}

internal sealed class ReplyValidator : AbstractValidator<ReplyToTicketCommand>
{
    public ReplyValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(Ticket.MaxBody);
}

internal sealed class SuggestionValidator : AbstractValidator<SubmitSuggestionCommand>
{
    public SuggestionValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Suggestion.MaxTitle);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Ticket.MaxBody);
    }
}

internal sealed class AnswerSuggestionValidator : AbstractValidator<AnswerSuggestionCommand>
{
    public AnswerSuggestionValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(Suggestion.MaxNote);
    }
}

internal sealed class PageValidator : AbstractValidator<ListTicketsQuery>
{
    public PageValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class SuggestionPageValidator : AbstractValidator<ListSuggestionsQuery>
{
    public SuggestionPageValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>
/// Who may see what. Customers see their own company's tickets and suggestions; every company role may read and
/// write them except Viewer, who only reads. Support staff and admins see all of them.
/// </summary>
internal sealed class SupportScope(ICurrentUser me, ICompanyDirectory companies)
{
    public const int MaxOpenTicketsPerCompany = 20;

    public Guid UserId => me.UserId!.Value;

    public bool IsStaff => me.IsInRole(IdentityRoles.Support) || me.IsInRole(IdentityRoles.Admin);

    public async Task<Result<CompanyMembership>> MemberAsync(bool toWrite, CancellationToken ct)
    {
        if (me.UserId is not { } userId || await companies.MembershipOfAsync(userId, ct) is not { } membership) return SupportErrors.NoCompany;
        if (toWrite && membership.Role == "Viewer") return SupportErrors.NotAllowed;
        return membership;
    }

    /// <summary>The ticket, if the caller may act on it in the given role. Someone else's ticket looks the same as no ticket.</summary>
    public async Task<Result<Ticket>> TicketAsync(ISupportStore store, Guid ticketId, bool asStaff, bool toWrite, CancellationToken ct)
    {
        if (asStaff)
        {
            if (!IsStaff) return SupportErrors.StaffOnly;
            return await store.TicketAsync(ticketId, ct) is { } any ? any : SupportErrors.TicketNotFound;
        }
        var member = await MemberAsync(toWrite, ct);
        if (member.IsFailure) return member.Error!;
        var ticket = await store.TicketAsync(ticketId, ct);
        return ticket is not null && ticket.CompanyId == member.Value.CompanyId ? ticket : SupportErrors.TicketNotFound;
    }
}

internal sealed class SupportViews(IUserDirectory users, ICompanyDirectory companies)
{
    public static string NewTicketNumber(DateTimeOffset at)
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var suffix = new string(Enumerable.Range(0, 5).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        return $"T-{at:yyMMdd}-{suffix}";
    }

    /// <summary>
    /// Staff appear to customers as "Support" with their first name only; customers see each other's full names.
    /// Staff see everyone's full name.
    /// </summary>
    public async Task<TicketDto> TicketAsync(Ticket ticket, bool forStaff, CancellationToken ct)
    {
        var people = await users.FindManyAsync(ticket.Messages.Select(m => m.AuthorUserId).Distinct().ToList(), ct);
        string Name(TicketMessage m)
        {
            var name = people.TryGetValue(m.AuthorUserId, out var person) ? person.FullName : "—";
            if (!m.FromStaff || forStaff) return name;
            var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return string.IsNullOrEmpty(first) ? "Support" : $"{first} (Support)";
        }
        var company = forStaff ? (await companies.FindAsync(ticket.CompanyId, ct))?.Name : null;
        return new TicketDto(ticket.Id, ticket.Number, ticket.Subject, ticket.Category, ticket.Status, ticket.OpenedAt, company,
            ticket.Messages.OrderBy(m => m.PostedAt).Select(m => new TicketMessageDto(m.Id, Name(m), m.FromStaff, m.Body, m.PostedAt)).ToList());
    }

    public async Task<string?> CompanyNameAsync(Guid companyId, Dictionary<Guid, string?> cache, CancellationToken ct)
    {
        if (!cache.TryGetValue(companyId, out var name)) cache[companyId] = name = (await companies.FindAsync(companyId, ct))?.Name;
        return name;
    }

    public static TicketSummaryDto Summary(Ticket t, string? company) =>
        new(t.Id, t.Number, t.Subject, t.Category, t.Status, t.OpenedAt, t.LastActivityAt, company);

    public static SuggestionDto Suggestion(Suggestion s, string? company) =>
        new(s.Id, s.Title, s.Body, s.Status, s.StaffNote, s.CreatedAt, company);
}

internal sealed class OpenTicketHandler(SupportScope scope, ISupportStore store, SupportViews views, TimeProvider clock)
    : ICommandHandler<OpenTicketCommand, Result<TicketDto>>
{
    public async Task<Result<TicketDto>> HandleAsync(OpenTicketCommand command, CancellationToken ct)
    {
        var member = await scope.MemberAsync(toWrite: true, ct);
        if (member.IsFailure) return member.Error!;
        if (await store.OpenTicketCountAsync(member.Value.CompanyId, ct) >= SupportScope.MaxOpenTicketsPerCompany) return SupportErrors.TooManyOpenTickets;
        var now = clock.GetUtcNow();
        var ticket = Ticket.Open(SupportViews.NewTicketNumber(now), member.Value.CompanyId, scope.UserId, command.Subject, command.Category, command.Body, now);
        store.Add(ticket);
        await store.SaveAsync(ct);
        return await views.TicketAsync(ticket, forStaff: false, ct);
    }
}

internal sealed partial class ReplyToTicketHandler(SupportScope scope, ISupportStore store, SupportViews views, IUserDirectory users, IEmailSender email,
    PortalLinks links, TimeProvider clock, ILogger<ReplyToTicketHandler> logger) : ICommandHandler<ReplyToTicketCommand, Result<TicketDto>>
{
    public async Task<Result<TicketDto>> HandleAsync(ReplyToTicketCommand command, CancellationToken ct)
    {
        var found = await scope.TicketAsync(store, command.TicketId, command.AsStaff, toWrite: true, ct);
        if (found.IsFailure) return found.Error!;
        var ticket = found.Value;
        var now = clock.GetUtcNow();
        var added = command.AsStaff ? ticket.StaffReply(scope.UserId, command.Body, now) : ticket.CustomerReply(scope.UserId, command.Body, now);
        if (added.IsFailure) return added.Error!;
        await store.SaveAsync(ct);

        if (command.AsStaff && await users.FindAsync(ticket.OpenedBy, ct) is { } opener)
        {
            // The answer is saved and shown in the portal either way; the email is only a heads-up.
            try
            {
                await email.SendAsync(SupportEmails.Answered(opener.Email, ticket.Number, ticket.Subject, links.To($"support/{ticket.Id}")), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                EmailFailed(logger, ex, ticket.Id);
            }
        }
        return await views.TicketAsync(ticket, command.AsStaff, ct);
    }

    [LoggerMessage(LogLevel.Warning, "Could not email the answer to ticket {TicketId}")]
    private static partial void EmailFailed(ILogger logger, Exception ex, Guid ticketId);
}

internal sealed class CloseTicketHandler(SupportScope scope, ISupportStore store, SupportViews views, TimeProvider clock)
    : ICommandHandler<CloseTicketCommand, Result<TicketDto>>
{
    public async Task<Result<TicketDto>> HandleAsync(CloseTicketCommand command, CancellationToken ct)
    {
        var found = await scope.TicketAsync(store, command.TicketId, command.AsStaff, toWrite: true, ct);
        if (found.IsFailure) return found.Error!;
        var closed = found.Value.Close(clock.GetUtcNow());
        if (closed.IsFailure) return closed.Error!;
        await store.SaveAsync(ct);
        return await views.TicketAsync(found.Value, command.AsStaff, ct);
    }
}

internal sealed class GetTicketHandler(SupportScope scope, ISupportStore store, SupportViews views) : IQueryHandler<GetTicketQuery, Result<TicketDto>>
{
    public async Task<Result<TicketDto>> HandleAsync(GetTicketQuery query, CancellationToken ct)
    {
        var found = await scope.TicketAsync(store, query.TicketId, query.AsStaff, toWrite: false, ct);
        return found.IsFailure ? found.Error! : await views.TicketAsync(found.Value, query.AsStaff, ct);
    }
}

internal sealed class MyTicketsHandler(SupportScope scope, ISupportStore store) : IQueryHandler<MyTicketsQuery, Result<IReadOnlyList<TicketSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<TicketSummaryDto>>> HandleAsync(MyTicketsQuery query, CancellationToken ct)
    {
        var member = await scope.MemberAsync(toWrite: false, ct);
        if (member.IsFailure) return member.Error!;
        return (await store.CompanyTicketsAsync(member.Value.CompanyId, ct)).Select(t => SupportViews.Summary(t, null)).ToList();
    }
}

internal sealed class ListTicketsHandler(SupportScope scope, ISupportStore store, SupportViews views)
    : IQueryHandler<ListTicketsQuery, Result<PagedResult<TicketSummaryDto>>>
{
    public async Task<Result<PagedResult<TicketSummaryDto>>> HandleAsync(ListTicketsQuery query, CancellationToken ct)
    {
        if (!scope.IsStaff) return SupportErrors.StaffOnly;
        var page = await store.TicketsAsync(query.Status, query.Page, query.PageSize, ct);
        var names = new Dictionary<Guid, string?>();
        var items = new List<TicketSummaryDto>();
        foreach (var t in page.Items) items.Add(SupportViews.Summary(t, await views.CompanyNameAsync(t.CompanyId, names, ct)));
        return new PagedResult<TicketSummaryDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class SubmitSuggestionHandler(SupportScope scope, ISupportStore store, TimeProvider clock)
    : ICommandHandler<SubmitSuggestionCommand, Result<SuggestionDto>>
{
    public async Task<Result<SuggestionDto>> HandleAsync(SubmitSuggestionCommand command, CancellationToken ct)
    {
        var member = await scope.MemberAsync(toWrite: true, ct);
        if (member.IsFailure) return member.Error!;
        var suggestion = Suggestion.Submit(member.Value.CompanyId, scope.UserId, command.Title, command.Body, clock.GetUtcNow());
        store.Add(suggestion);
        await store.SaveAsync(ct);
        return SupportViews.Suggestion(suggestion, null);
    }
}

internal sealed class MySuggestionsHandler(SupportScope scope, ISupportStore store) : IQueryHandler<MySuggestionsQuery, Result<IReadOnlyList<SuggestionDto>>>
{
    public async Task<Result<IReadOnlyList<SuggestionDto>>> HandleAsync(MySuggestionsQuery query, CancellationToken ct)
    {
        var member = await scope.MemberAsync(toWrite: false, ct);
        if (member.IsFailure) return member.Error!;
        return (await store.CompanySuggestionsAsync(member.Value.CompanyId, ct)).Select(s => SupportViews.Suggestion(s, null)).ToList();
    }
}

internal sealed class ListSuggestionsHandler(SupportScope scope, ISupportStore store, SupportViews views)
    : IQueryHandler<ListSuggestionsQuery, Result<PagedResult<SuggestionDto>>>
{
    public async Task<Result<PagedResult<SuggestionDto>>> HandleAsync(ListSuggestionsQuery query, CancellationToken ct)
    {
        if (!scope.IsStaff) return SupportErrors.StaffOnly;
        var page = await store.SuggestionsAsync(query.Status, query.Page, query.PageSize, ct);
        var names = new Dictionary<Guid, string?>();
        var items = new List<SuggestionDto>();
        foreach (var s in page.Items) items.Add(SupportViews.Suggestion(s, await views.CompanyNameAsync(s.CompanyId, names, ct)));
        return new PagedResult<SuggestionDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class AnswerSuggestionHandler(SupportScope scope, ISupportStore store, SupportViews views, TimeProvider clock)
    : ICommandHandler<AnswerSuggestionCommand, Result<SuggestionDto>>
{
    public async Task<Result<SuggestionDto>> HandleAsync(AnswerSuggestionCommand command, CancellationToken ct)
    {
        if (!scope.IsStaff) return SupportErrors.StaffOnly;
        if (await store.SuggestionAsync(command.SuggestionId, ct) is not { } suggestion) return SupportErrors.SuggestionNotFound;
        suggestion.Answer(command.Status, command.Note, clock.GetUtcNow());
        await store.SaveAsync(ct);
        return SupportViews.Suggestion(suggestion, await views.CompanyNameAsync(suggestion.CompanyId, [], ct));
    }
}

/// <summary>Support email texts, Arabic first then English. Ticket contents stay in the portal, behind sign-in.</summary>
internal static class SupportEmails
{
    public static EmailMessage Answered(string to, string number, string subject, string link) => new(to,
        $"رد على تذكرتك {number} | Reply to your ticket {number}",
        $"""
        فريق الدعم رد على تذكرتك "{subject}". تقدر تقرا الرد وترد عليه من هنا:
        {link}

        Support replied to your ticket "{subject}". Read and answer it here:
        {link}
        """);
}

public static class DependencyInjection
{
    public static IServiceCollection AddSupportApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddHandlersFrom(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<SupportScope>();
        services.AddScoped<SupportViews>();
        return services;
    }
}
