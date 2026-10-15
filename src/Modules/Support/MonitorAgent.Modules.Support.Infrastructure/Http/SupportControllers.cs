using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
}
