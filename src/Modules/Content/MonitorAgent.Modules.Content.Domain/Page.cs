using System.Text.RegularExpressions;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Content.Domain;

/// <summary>Draft: being edited. InReview: waiting on a reviewer. Published: what the website shows. Superseded: published before.</summary>
public enum VersionStatus { Draft, InReview, Published, Superseded }

/// <summary>
/// What a page says in both languages. Bodies are plain text with a small markdown subset (## headings, - lists,
/// [links](https://...)) that the website renders itself, so no HTML written here ever reaches a browser as HTML.
/// </summary>
public sealed record PageContent(string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn, string BodyAr, string BodyEn)
{
    public const int MaxTitle = 150;
    public const int MaxDescription = 300;
    public const int MaxBody = 20_000;

    public PageContent Trimmed() => new(TitleAr.Trim(), TitleEn.Trim(), DescriptionAr.Trim(), DescriptionEn.Trim(), BodyAr.Trim(), BodyEn.Trim());
}

public sealed class PageVersion : Entity
{
    private PageVersion() { }

    public int Number { get; private set; }
    public VersionStatus Status { get; private set; }
    public string TitleAr { get; private set; } = "";
    public string TitleEn { get; private set; } = "";
    public string DescriptionAr { get; private set; } = "";
    public string DescriptionEn { get; private set; } = "";
    public string BodyAr { get; private set; } = "";
    public string BodyEn { get; private set; } = "";
    /// <summary>Whoever changed the text last; they may not approve it.</summary>
    public Guid EditedBy { get; private set; }
    public DateTimeOffset EditedAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    /// <summary>Why the reviewer sent it back, until the next edit.</summary>
    public string? ReviewNote { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public PageContent Content => new(TitleAr, TitleEn, DescriptionAr, DescriptionEn, BodyAr, BodyEn);

    internal static PageVersion Create(int number, PageContent content, Guid editor, DateTimeOffset now)
    {
        var version = new PageVersion { Number = number, Status = VersionStatus.Draft };
        version.Edit(content, editor, now);
        return version;
    }

    internal void Edit(PageContent content, Guid editor, DateTimeOffset now)
    {
        (TitleAr, TitleEn, DescriptionAr, DescriptionEn, BodyAr, BodyEn) = content.Trimmed();
        EditedBy = editor;
        EditedAt = now;
        ReviewNote = null;
    }

    internal void SetStatus(VersionStatus status) => Status = status;

    internal void Review(Guid reviewer, DateTimeOffset now, VersionStatus outcome, string? note)
    {
        ReviewedBy = reviewer;
        ReviewedAt = now;
        ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = outcome;
        if (outcome == VersionStatus.Published) PublishedAt = now;
    }
}

public sealed record PagePublished(Guid PageId, string Slug, int Version) : DomainEvent;

/// <summary>
/// A page of the public website and every version of it. Editors change one open version at a time, a reviewer who did
/// not write it approves it, and only then does the website show it. Older versions stay for history and restore.
/// </summary>
public sealed partial class Page : AggregateRoot
{
    public const int MaxSlug = 60;
    public const int MaxVersions = 200;
    public const int MaxNote = 1000;

    private readonly List<PageVersion> _versions = [];

    private Page() { }

    public string Slug { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int? PublishedVersion { get; private set; }
    public IReadOnlyList<PageVersion> Versions => _versions;

    /// <summary>The version being worked on (Draft or InReview), if any. There is never more than one.</summary>
    public PageVersion? Open => _versions.LastOrDefault(v => v.Status is VersionStatus.Draft or VersionStatus.InReview);

    public PageVersion? Published => _versions.SingleOrDefault(v => v.Status == VersionStatus.Published);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public static bool IsValidSlug(string? slug) => slug is { Length: > 0 and <= MaxSlug } && SlugPattern().IsMatch(slug);

    public static Page Create(string slug, PageContent content, Guid editor, DateTimeOffset now)
    {
        var page = new Page { Slug = slug, CreatedAt = now, UpdatedAt = now };
        page._versions.Add(PageVersion.Create(1, content, editor, now));
        return page;
    }

    /// <summary>Changes the open draft, or starts a new one from the given text. Not while a version waits for review.</summary>
    public Result SaveDraft(PageContent content, Guid editor, DateTimeOffset now)
    {
        switch (Open)
        {
            case { Status: VersionStatus.InReview }:
                return ContentErrors.InReview;
            case { } draft:
                draft.Edit(content, editor, now);
                break;
            default:
                if (_versions.Count >= MaxVersions) return ContentErrors.TooManyVersions;
                _versions.Add(PageVersion.Create(_versions.Max(v => v.Number) + 1, content, editor, now));
                break;
        }
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Starts a draft from an older version's text.</summary>
    public Result Restore(int number, Guid editor, DateTimeOffset now) =>
        _versions.SingleOrDefault(v => v.Number == number) is { } old ? SaveDraft(old.Content, editor, now) : ContentErrors.VersionNotFound;

    public Result Submit(DateTimeOffset now)
    {
        if (Open is not { Status: VersionStatus.Draft } draft) return ContentErrors.NoDraft;
        if (draft.BodyAr.Length == 0 || draft.BodyEn.Length == 0) return ContentErrors.Incomplete;
        draft.SetStatus(VersionStatus.InReview);
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>The editor takes a version back from review to change it.</summary>
    public Result Withdraw(DateTimeOffset now)
    {
        if (Open is not { Status: VersionStatus.InReview } version) return ContentErrors.NotInReview;
        version.SetStatus(VersionStatus.Draft);
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Publishes the version under review. Four eyes: whoever edited it last may not approve it.</summary>
    public Result Approve(Guid reviewer, DateTimeOffset now)
    {
        if (Open is not { Status: VersionStatus.InReview } version) return ContentErrors.NotInReview;
        if (version.EditedBy == reviewer) return ContentErrors.OwnVersion;
        Published?.SetStatus(VersionStatus.Superseded);
        version.Review(reviewer, now, VersionStatus.Published, note: null);
        PublishedVersion = version.Number;
        UpdatedAt = now;
        Raise(new PagePublished(Id, Slug, version.Number));
        return Result.Success();
    }

    /// <summary>Sends the version back to its editor as a draft, with the reason.</summary>
    public Result Reject(Guid reviewer, string note, DateTimeOffset now)
    {
        if (Open is not { Status: VersionStatus.InReview } version) return ContentErrors.NotInReview;
        if (version.EditedBy == reviewer) return ContentErrors.OwnVersion;
        version.Review(reviewer, now, VersionStatus.Draft, note);
        UpdatedAt = now;
        return Result.Success();
    }
}

public static class ContentErrors
{
    public static readonly Error PageNotFound = Error.NotFound("PAGE_NOT_FOUND", "No such page.");
    public static readonly Error VersionNotFound = Error.NotFound("VERSION_NOT_FOUND", "No such version of this page.");
    public static readonly Error SlugTaken = Error.Conflict("SLUG_TAKEN", "Another page already uses this address.");
    public static readonly Error InReview = Error.Conflict("PAGE_IN_REVIEW", "This page is waiting for review. Withdraw it to make changes.");
    public static readonly Error Incomplete = Error.Validation("PAGE_INCOMPLETE", "Write the page in both Arabic and English before sending it for review.");
    public static readonly Error NoDraft = Error.Conflict("NO_DRAFT", "There is no draft to send for review.");
    public static readonly Error NotInReview = Error.Conflict("NOT_IN_REVIEW", "Nothing is waiting for review on this page.");
    public static readonly Error OwnVersion = Error.Forbidden("OWN_VERSION", "Someone other than the last editor has to review this version.");
    public static readonly Error Changed = Error.Conflict("PAGE_CHANGED", "Someone else changed this page just now. Reload it and try again.");
    public static readonly Error TooManyVersions = Error.Conflict("TOO_MANY_VERSIONS", "This page has too many versions.");
    public static readonly Error EditorsOnly = Error.Forbidden("FORBIDDEN", "Only content editors can do this.");
    public static readonly Error ReviewersOnly = Error.Forbidden("FORBIDDEN", "Only content reviewers can do this.");
}
