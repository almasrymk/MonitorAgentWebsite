using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Content.Domain;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Content.Application;

public interface IContentStore
{
    Task<Page?> PageAsync(Guid id, CancellationToken ct);
    Task<Page?> PageBySlugAsync(string slug, CancellationToken ct);
    Task<bool> SlugTakenAsync(string slug, CancellationToken ct);
    Task<IReadOnlyList<Page>> PagesAsync(CancellationToken ct);
    void Add(Page page);
    /// <summary>Fails with <see cref="ContentErrors.Changed"/> when someone else changed the page since it was loaded.</summary>
    Task<Result> SaveAsync(CancellationToken ct);
}

public sealed record PublicPageDto(string Slug, int Version, DateTimeOffset PublishedAt, string TitleAr, string TitleEn, string DescriptionAr,
    string DescriptionEn, string BodyAr, string BodyEn);
public sealed record PageSummaryDto(Guid Id, string Slug, string TitleAr, string TitleEn, int? PublishedVersion, VersionStatus? OpenStatus,
    DateTimeOffset UpdatedAt);
public sealed record PageVersionDto(int Number, VersionStatus Status, string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn,
    string BodyAr, string BodyEn, string EditedBy, DateTimeOffset EditedAt, string? ReviewedBy, DateTimeOffset? ReviewedAt, string? ReviewNote,
    DateTimeOffset? PublishedAt);
public sealed record PageDto(Guid Id, string Slug, int? PublishedVersion, IReadOnlyList<PageVersionDto> Versions);

public sealed record GetPublishedPageQuery(string Slug) : IQuery<Result<PublicPageDto>>;
public sealed record ListPagesQuery : IQuery<Result<IReadOnlyList<PageSummaryDto>>>;
public sealed record GetPageQuery(Guid PageId) : IQuery<Result<PageDto>>;
public sealed record CreatePageCommand(string Slug, PageContent Content) : ICommand<Result<PageDto>>, IAuditedCommand
{
    public string AuditAction => "content.create";
    public string AuditTarget => $"page:{Slug}";
}
public sealed record SaveDraftCommand(Guid PageId, PageContent Content) : ICommand<Result<PageDto>>;
public sealed record RestoreVersionCommand(Guid PageId, int Number) : ICommand<Result<PageDto>>, IAuditedCommand
{
    public string AuditAction => "content.restore";
    public string AuditTarget => $"page:{PageId}";
    public string? AuditDetail => $"version {Number}";
}
public sealed record SubmitPageCommand(Guid PageId) : ICommand<Result<PageDto>>;
public sealed record WithdrawPageCommand(Guid PageId) : ICommand<Result<PageDto>>;
public sealed record ApprovePageCommand(Guid PageId) : ICommand<Result<PageDto>>, IAuditedCommand
{
    public string AuditAction => "content.approve";
    public string AuditTarget => $"page:{PageId}";
}
public sealed record RejectPageCommand(Guid PageId, string Note) : ICommand<Result<PageDto>>, IAuditedCommand
{
    public string AuditAction => "content.reject";
    public string AuditTarget => $"page:{PageId}";
    public string? AuditDetail => Note;
}

internal sealed class PageContentValidator : AbstractValidator<PageContent>
{
    public PageContentValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(PageContent.MaxTitle);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(PageContent.MaxTitle);
        RuleFor(x => x.DescriptionAr).NotNull().MaximumLength(PageContent.MaxDescription);
        RuleFor(x => x.DescriptionEn).NotNull().MaximumLength(PageContent.MaxDescription);
        RuleFor(x => x.BodyAr).NotNull().MaximumLength(PageContent.MaxBody);
        RuleFor(x => x.BodyEn).NotNull().MaximumLength(PageContent.MaxBody);
    }
}

internal sealed class CreatePageValidator : AbstractValidator<CreatePageCommand>
{
    public CreatePageValidator()
    {
        RuleFor(x => x.Slug).Must(Page.IsValidSlug)
            .WithMessage($"Use lowercase letters, digits and single dashes, up to {Page.MaxSlug} characters.");
        RuleFor(x => x.Content).NotNull().SetValidator(new PageContentValidator());
    }
}

internal sealed class SaveDraftValidator : AbstractValidator<SaveDraftCommand>
{
    public SaveDraftValidator() => RuleFor(x => x.Content).NotNull().SetValidator(new PageContentValidator());
}

internal sealed class RejectPageValidator : AbstractValidator<RejectPageCommand>
{
    public RejectPageValidator() => RuleFor(x => x.Note).NotEmpty().MaximumLength(Page.MaxNote);
}

/// <summary>Editors write and send for review; reviewers publish or send back. Admins may do both, but never review their own edit.</summary>
internal sealed class ContentScope(ICurrentUser me)
{
    public Guid UserId => me.UserId!.Value;

    public bool IsEditor => me.IsInRole(IdentityRoles.ContentEditor) || me.IsInRole(IdentityRoles.Admin);

    public bool IsReviewer => me.IsInRole(IdentityRoles.ContentReviewer) || me.IsInRole(IdentityRoles.Admin);

    public async Task<Result<Page>> PageAsync(IContentStore store, Guid pageId, bool editor, bool reviewer, CancellationToken ct)
    {
        if (editor && !IsEditor) return ContentErrors.EditorsOnly;
        if (reviewer && !IsReviewer) return ContentErrors.ReviewersOnly;
        if (!IsEditor && !IsReviewer) return ContentErrors.EditorsOnly;
        return await store.PageAsync(pageId, ct) is { } page ? page : ContentErrors.PageNotFound;
    }
}

internal sealed class ContentViews(IUserDirectory users)
{
    public async Task<PageDto> PageAsync(Page page, CancellationToken ct)
    {
        var ids = page.Versions.Select(v => v.EditedBy).Concat(page.Versions.Where(v => v.ReviewedBy is not null).Select(v => v.ReviewedBy!.Value));
        var people = await users.FindManyAsync(ids.Distinct().ToList(), ct);
        string Name(Guid id) => people.TryGetValue(id, out var person) ? person.FullName : "—";
        var versions = page.Versions.OrderByDescending(v => v.Number).Select(v => new PageVersionDto(v.Number, v.Status, v.TitleAr, v.TitleEn,
            v.DescriptionAr, v.DescriptionEn, v.BodyAr, v.BodyEn, Name(v.EditedBy), v.EditedAt, v.ReviewedBy is { } r ? Name(r) : null, v.ReviewedAt,
            v.ReviewNote, v.PublishedAt)).ToList();
        return new PageDto(page.Id, page.Slug, page.PublishedVersion, versions);
    }

    public static PageSummaryDto Summary(Page page)
    {
        var latest = page.Open ?? page.Published ?? page.Versions[^1];
        return new PageSummaryDto(page.Id, page.Slug, latest.TitleAr, latest.TitleEn, page.PublishedVersion, page.Open?.Status, page.UpdatedAt);
    }
}

internal sealed class GetPublishedPageHandler(IContentStore store) : IQueryHandler<GetPublishedPageQuery, Result<PublicPageDto>>
{
    public async Task<Result<PublicPageDto>> HandleAsync(GetPublishedPageQuery query, CancellationToken ct)
    {
        if (!Page.IsValidSlug(query.Slug) || await store.PageBySlugAsync(query.Slug, ct) is not { Published: { } v } page) return ContentErrors.PageNotFound;
        return new PublicPageDto(page.Slug, v.Number, v.PublishedAt!.Value, v.TitleAr, v.TitleEn, v.DescriptionAr, v.DescriptionEn, v.BodyAr, v.BodyEn);
    }
}

internal sealed class ListPagesHandler(ContentScope scope, IContentStore store) : IQueryHandler<ListPagesQuery, Result<IReadOnlyList<PageSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<PageSummaryDto>>> HandleAsync(ListPagesQuery query, CancellationToken ct)
    {
        if (!scope.IsEditor && !scope.IsReviewer) return ContentErrors.EditorsOnly;
        return (await store.PagesAsync(ct)).Select(ContentViews.Summary).ToList();
    }
}

internal sealed class GetPageHandler(ContentScope scope, IContentStore store, ContentViews views) : IQueryHandler<GetPageQuery, Result<PageDto>>
{
    public async Task<Result<PageDto>> HandleAsync(GetPageQuery query, CancellationToken ct)
    {
        var page = await scope.PageAsync(store, query.PageId, editor: false, reviewer: false, ct);
        return page.IsFailure ? page.Error! : await views.PageAsync(page.Value, ct);
    }
}

internal sealed class CreatePageHandler(ContentScope scope, IContentStore store, ContentViews views, TimeProvider clock)
    : ICommandHandler<CreatePageCommand, Result<PageDto>>
{
    public async Task<Result<PageDto>> HandleAsync(CreatePageCommand command, CancellationToken ct)
    {
        if (!scope.IsEditor) return ContentErrors.EditorsOnly;
        if (await store.SlugTakenAsync(command.Slug, ct)) return ContentErrors.SlugTaken;
        var page = Page.Create(command.Slug, command.Content, scope.UserId, clock.GetUtcNow());
        store.Add(page);
        var saved = await store.SaveAsync(ct);
        if (saved.IsFailure) return saved.Error!;
        return await views.PageAsync(page, ct);
    }
}

/// <summary>Loads the page for the caller's role, applies one workflow step, saves and returns the page.</summary>
internal abstract class PageStepHandler<TCommand>(ContentScope scope, IContentStore store, ContentViews views, TimeProvider clock, bool asReviewer)
    : ICommandHandler<TCommand, Result<PageDto>> where TCommand : ICommand<Result<PageDto>>
{
    protected abstract Guid PageId(TCommand command);

    protected abstract Result Apply(Page page, TCommand command, Guid userId, DateTimeOffset now);

    public async Task<Result<PageDto>> HandleAsync(TCommand command, CancellationToken ct)
    {
        var found = await scope.PageAsync(store, PageId(command), editor: !asReviewer, reviewer: asReviewer, ct);
        if (found.IsFailure) return found.Error!;
        var applied = Apply(found.Value, command, scope.UserId, clock.GetUtcNow());
        if (applied.IsFailure) return applied.Error!;
        var saved = await store.SaveAsync(ct);
        if (saved.IsFailure) return saved.Error!;
        return await views.PageAsync(found.Value, ct);
    }
}

internal sealed class SaveDraftHandler(ContentScope scope, IContentStore store, ContentViews views, TimeProvider clock)
    : PageStepHandler<SaveDraftCommand>(scope, store, views, clock, asReviewer: false)
{
    protected override Guid PageId(SaveDraftCommand command) => command.PageId;
    protected override Result Apply(Page page, SaveDraftCommand command, Guid userId, DateTimeOffset now) => page.SaveDraft(command.Content, userId, now);
}

internal sealed class RestoreVersionHandler(ContentScope scope, IContentStore store, ContentViews views, TimeProvider clock)
    : PageStepHandler<RestoreVersionCommand>(scope, store, views, clock, asReviewer: false)
{
    protected override Guid PageId(RestoreVersionCommand command) => command.PageId;
    protected override Result Apply(Page page, RestoreVersionCommand command, Guid userId, DateTimeOffset now) => page.Restore(command.Number, userId, now);
}

internal sealed class SubmitPageHandler(ContentScope scope, IContentStore store, ContentViews views, TimeProvider clock)
    : PageStepHandler<SubmitPageCommand>(scope, store, views, clock, asReviewer: false)
{
    protected override Guid PageId(SubmitPageCommand command) => command.PageId;
    protected override Result Apply(Page page, SubmitPageCommand command, Guid userId, DateTimeOffset now) => page.Submit(now);
}

internal sealed class WithdrawPageHandler(ContentScope scope, IContentStore store, ContentViews views, TimeProvider clock)
    : PageStepHandler<WithdrawPageCommand>(scope, store, views, clock, asReviewer: false)
{
    protected override Guid PageId(WithdrawPageCommand command) => command.PageId;
    protected override Result Apply(Page page, WithdrawPageCommand command, Guid userId, DateTimeOffset now) => page.Withdraw(now);
}

internal sealed class ApprovePageHandler(ContentScope scope, IContentStore store, ContentViews views, TimeProvider clock)
    : PageStepHandler<ApprovePageCommand>(scope, store, views, clock, asReviewer: true)
{
    protected override Guid PageId(ApprovePageCommand command) => command.PageId;
    protected override Result Apply(Page page, ApprovePageCommand command, Guid userId, DateTimeOffset now) => page.Approve(userId, now);
}

internal sealed class RejectPageHandler(ContentScope scope, IContentStore store, ContentViews views, TimeProvider clock)
    : PageStepHandler<RejectPageCommand>(scope, store, views, clock, asReviewer: true)
{
    protected override Guid PageId(RejectPageCommand command) => command.PageId;
    protected override Result Apply(Page page, RejectPageCommand command, Guid userId, DateTimeOffset now) => page.Reject(userId, command.Note, now);
}

public static class DependencyInjection
{
    public static IServiceCollection AddContentApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddHandlersFrom(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<ContentScope>();
        services.AddScoped<ContentViews>();
        return services;
    }
}
