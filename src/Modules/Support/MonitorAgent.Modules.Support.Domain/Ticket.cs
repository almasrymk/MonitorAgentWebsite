using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Support.Domain;

public enum TicketCategory { Technical, Licensing, Billing, Other }

/// <summary>Open: waiting on support. Answered: waiting on the customer. Closed: done, reopened by a new customer message.</summary>
public enum TicketStatus { Open, Answered, Closed }

public sealed class TicketMessage : Entity
{
    private TicketMessage() { }

    public Guid AuthorUserId { get; private set; }
    public bool FromStaff { get; private set; }
    public string Body { get; private set; } = "";
    public DateTimeOffset PostedAt { get; private set; }

    internal static TicketMessage Create(Guid author, bool fromStaff, string body, DateTimeOffset at) =>
        new() { AuthorUserId = author, FromStaff = fromStaff, Body = body.Trim(), PostedAt = at };
}

/// <summary>A customer's support request and its conversation. Belongs to one company; every member of it can follow it.</summary>
public sealed class Ticket : AggregateRoot
{
    public const int MaxSubject = 150;
    public const int MaxBody = 5000;
    public const int MaxMessages = 200;

    private readonly List<TicketMessage> _messages = [];

    private Ticket() { }

    public string Number { get; private set; } = "";
    public Guid CompanyId { get; private set; }
    public Guid OpenedBy { get; private set; }
    public string Subject { get; private set; } = "";
    public TicketCategory Category { get; private set; }
    public TicketStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public IReadOnlyList<TicketMessage> Messages => _messages;

    public static Ticket Open(string number, Guid companyId, Guid userId, string subject, TicketCategory category, string body, DateTimeOffset now)
    {
        var ticket = new Ticket
        {
            Number = number,
            CompanyId = companyId,
            OpenedBy = userId,
            Subject = subject.Trim(),
            Category = category,
            Status = TicketStatus.Open,
            OpenedAt = now,
            LastActivityAt = now,
        };
        ticket._messages.Add(TicketMessage.Create(userId, fromStaff: false, body, now));
        return ticket;
    }

    /// <summary>A customer message. On a closed ticket it reopens it, so a customer is never left without a way to answer.</summary>
    public Result CustomerReply(Guid userId, string body, DateTimeOffset now) => Add(userId, fromStaff: false, body, now, TicketStatus.Open);

    public Result StaffReply(Guid userId, string body, DateTimeOffset now) => Add(userId, fromStaff: true, body, now, TicketStatus.Answered);

    public Result Close(DateTimeOffset now)
    {
        if (Status == TicketStatus.Closed) return SupportErrors.AlreadyClosed;
        Status = TicketStatus.Closed;
        LastActivityAt = now;
        return Result.Success();
    }

    private Result Add(Guid userId, bool fromStaff, string body, DateTimeOffset now, TicketStatus next)
    {
        if (_messages.Count >= MaxMessages) return SupportErrors.TooManyMessages;
        _messages.Add(TicketMessage.Create(userId, fromStaff, body, now));
        Status = next;
        LastActivityAt = now;
        return Result.Success();
    }
}

public enum SuggestionStatus { New, Planned, Done, Declined }

/// <summary>A product idea from a customer. Staff answer it by setting a status and an optional note the customer sees.</summary>
public sealed class Suggestion : AggregateRoot
{
    public const int MaxTitle = 150;
    public const int MaxNote = 1000;

    private Suggestion() { }

    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    public SuggestionStatus Status { get; private set; }
    public string? StaffNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AnsweredAt { get; private set; }

    public static Suggestion Submit(Guid companyId, Guid userId, string title, string body, DateTimeOffset now) => new()
    {
        CompanyId = companyId,
        UserId = userId,
        Title = title.Trim(),
        Body = body.Trim(),
        Status = SuggestionStatus.New,
        CreatedAt = now,
    };

    public void Answer(SuggestionStatus status, string? note, DateTimeOffset now)
    {
        Status = status;
        StaffNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        AnsweredAt = now;
    }
}

/// <summary>Error codes the Support endpoints return. They are part of the API contract.</summary>
public static class SupportErrors
{
    public static readonly Error NoCompany = Error.Forbidden("NO_COMPANY", "Create or join a company first.");
    public static readonly Error NotAllowed = Error.Forbidden("NOT_ALLOWED", "Your role in the company can't do this.");
    public static readonly Error TicketNotFound = Error.NotFound("TICKET_NOT_FOUND", "No such ticket.");
    public static readonly Error SuggestionNotFound = Error.NotFound("SUGGESTION_NOT_FOUND", "No such suggestion.");
    public static readonly Error DemoRequestNotFound = Error.NotFound("DEMO_REQUEST_NOT_FOUND", "No such demo request.");
    public static readonly Error AlreadyClosed = Error.Conflict("TICKET_CLOSED", "The ticket is already closed.");
    public static readonly Error TooManyMessages = Error.Conflict("TICKET_TOO_LONG", "This ticket has too many messages. Open a new one.");
    public static readonly Error TooManyOpenTickets = Error.Conflict("TOO_MANY_OPEN_TICKETS", "Your company has too many open tickets. Close some first.");
    public static readonly Error StaffOnly = Error.Forbidden("FORBIDDEN", "Only support staff can do this.");
}
