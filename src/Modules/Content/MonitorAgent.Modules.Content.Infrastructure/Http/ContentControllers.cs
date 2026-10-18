using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Content.Application;
using MonitorAgent.Modules.Content.Domain;
using MonitorAgent.Modules.Identity.Contracts;

namespace MonitorAgent.Modules.Content.Infrastructure.Http;

public sealed record CreatePageRequest(string Slug, PageContent Content);
public sealed record RejectRequest(string Note);

/// <summary>Published pages for the public website. Drafts and versions under review are never served here.</summary>
[ApiController]
[Route("api/v1/content")]
[AllowAnonymous]
public sealed class ContentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("pages/{slug}")]
    [ProducesResponseType<PublicPageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Page(string slug, CancellationToken ct)
    {
        var page = await dispatcher.QueryAsync(new GetPublishedPageQuery(slug), ct);
        // A minute is short enough that a newly published version shows up almost at once.
        if (page.IsSuccess) Response.Headers.CacheControl = "public, max-age=60";
        return page.ToActionResult();
    }
}

/// <summary>Content editors and reviewers (signed in with two-step verification): every page, its versions and the review steps.</summary>
[ApiController]
[Route("api/v1/admin/content/pages")]
[Authorize(Policy = AuthPolicies.Staff)]
public sealed class AdminContentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PageSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pages(CancellationToken ct) => (await dispatcher.QueryAsync(new ListPagesQuery(), ct)).ToActionResult();

    [HttpPost]
    [ProducesResponseType<PageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CreatePageRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new CreatePageCommand(request.Slug, request.Content), ct)).ToActionResult();

    [HttpGet("{pageId:guid}")]
    [ProducesResponseType<PageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Page(Guid pageId, CancellationToken ct) => (await dispatcher.QueryAsync(new GetPageQuery(pageId), ct)).ToActionResult();

    [HttpPut("{pageId:guid}/draft")]
    [ProducesResponseType<PageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDraft(Guid pageId, PageContent content, CancellationToken ct) =>
        (await dispatcher.SendAsync(new SaveDraftCommand(pageId, content), ct)).ToActionResult();

    [HttpPost("{pageId:guid}/versions/{number:int}/restore")]
    [ProducesResponseType<PageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(Guid pageId, int number, CancellationToken ct) =>
        (await dispatcher.SendAsync(new RestoreVersionCommand(pageId, number), ct)).ToActionResult();

    [HttpPost("{pageId:guid}/submit")]
    [ProducesResponseType<PageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid pageId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new SubmitPageCommand(pageId), ct)).ToActionResult();

    [HttpPost("{pageId:guid}/withdraw")]
    [ProducesResponseType<PageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw(Guid pageId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new WithdrawPageCommand(pageId), ct)).ToActionResult();

    [HttpPost("{pageId:guid}/approve")]
    [ProducesResponseType<PageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid pageId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ApprovePageCommand(pageId), ct)).ToActionResult();

    [HttpPost("{pageId:guid}/reject")]
    [ProducesResponseType<PageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid pageId, RejectRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new RejectPageCommand(pageId, request.Note), ct)).ToActionResult();
}
