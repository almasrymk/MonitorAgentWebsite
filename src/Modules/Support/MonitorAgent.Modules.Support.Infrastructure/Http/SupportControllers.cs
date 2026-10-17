using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Support.Application;
using MonitorAgent.Modules.Support.Domain;

namespace MonitorAgent.Modules.Support.Infrastructure.Http;

public sealed record OpenTicketRequest(string Subject, TicketCategory Category, string Body);
public sealed record MessageRequest(string Body);
public sealed record SuggestionRequest(string Title, string Body);
public sealed record AnswerSuggestionRequest(SuggestionStatus Status, string? Note);
public sealed record DemoRequestRequest(string Name, string Company, string Email, string? Phone, int? Sites, string? Message, string Language,
    string? Website);
public sealed record HandleDemoRequestRequest(DemoRequestStatus Status, string? Note);

/// <summary>The public site's "request a demo" form. No account needed; CSRF and a small per-address budget still apply.</summary>
[ApiController]
[Route("api/v1/demo-requests")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.PublicForm)]
public sealed class DemoRequestsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Submit(DemoRequestRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new SubmitDemoRequestCommand(request.Name, request.Company, request.Email, request.Phone, request.Sites,
            request.Message, request.Language, request.Website), ct)).ToAccepted();
}

/// <summary>The signed-in customer's company tickets and suggestions.</summary>
[ApiController]
[Route("api/v1/support")]
[Authorize(Policy = AuthPolicies.Customer)]
public sealed class SupportController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("tickets")]
    [ProducesResponseType<IReadOnlyList<TicketSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tickets(CancellationToken ct) => (await dispatcher.QueryAsync(new MyTicketsQuery(), ct)).ToActionResult();

    [HttpPost("tickets")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Open(OpenTicketRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new OpenTicketCommand(request.Subject, request.Category, request.Body), ct)).ToActionResult();

    [HttpGet("tickets/{ticketId:guid}")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ticket(Guid ticketId, CancellationToken ct) =>
        (await dispatcher.QueryAsync(new GetTicketQuery(ticketId, AsStaff: false), ct)).ToActionResult();

    [HttpPost("tickets/{ticketId:guid}/messages")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reply(Guid ticketId, MessageRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ReplyToTicketCommand(ticketId, request.Body, AsStaff: false), ct)).ToActionResult();

    [HttpPost("tickets/{ticketId:guid}/close")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(Guid ticketId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new CloseTicketCommand(ticketId, AsStaff: false), ct)).ToActionResult();

    [HttpGet("suggestions")]
    [ProducesResponseType<IReadOnlyList<SuggestionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Suggestions(CancellationToken ct) => (await dispatcher.QueryAsync(new MySuggestionsQuery(), ct)).ToActionResult();

    [HttpPost("suggestions")]
    [ProducesResponseType<SuggestionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Suggest(SuggestionRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new SubmitSuggestionCommand(request.Title, request.Body), ct)).ToActionResult();
}

/// <summary>Support staff and admins (signed in with two-step verification): every company's tickets and suggestions.</summary>
[ApiController]
[Route("api/v1/admin/support")]
[Authorize(Policy = AuthPolicies.Staff)]
public sealed class AdminSupportController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("tickets")]
    [ProducesResponseType<PagedResult<TicketSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tickets([FromQuery] TicketStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) => (await dispatcher.QueryAsync(new ListTicketsQuery(status, page, pageSize), ct)).ToActionResult();

    [HttpGet("tickets/{ticketId:guid}")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ticket(Guid ticketId, CancellationToken ct) =>
        (await dispatcher.QueryAsync(new GetTicketQuery(ticketId, AsStaff: true), ct)).ToActionResult();

    [HttpPost("tickets/{ticketId:guid}/messages")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reply(Guid ticketId, MessageRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ReplyToTicketCommand(ticketId, request.Body, AsStaff: true), ct)).ToActionResult();

    [HttpPost("tickets/{ticketId:guid}/close")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(Guid ticketId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new CloseTicketCommand(ticketId, AsStaff: true), ct)).ToActionResult();

    [HttpGet("suggestions")]
    [ProducesResponseType<PagedResult<SuggestionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Suggestions([FromQuery] SuggestionStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) => (await dispatcher.QueryAsync(new ListSuggestionsQuery(status, page, pageSize), ct)).ToActionResult();

    [HttpPut("suggestions/{suggestionId:guid}")]
    [ProducesResponseType<SuggestionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Answer(Guid suggestionId, AnswerSuggestionRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new AnswerSuggestionCommand(suggestionId, request.Status, request.Note), ct)).ToActionResult();

    [HttpGet("demo-requests")]
    [ProducesResponseType<PagedResult<DemoRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DemoRequests([FromQuery] DemoRequestStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) => (await dispatcher.QueryAsync(new ListDemoRequestsQuery(status, page, pageSize), ct)).ToActionResult();

    [HttpPut("demo-requests/{demoRequestId:guid}")]
    [ProducesResponseType<DemoRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HandleDemoRequest(Guid demoRequestId, HandleDemoRequestRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new HandleDemoRequestCommand(demoRequestId, request.Status, request.Note), ct)).ToActionResult();
}
