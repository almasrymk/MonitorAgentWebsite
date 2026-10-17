using FluentValidation;
using Microsoft.Extensions.Logging;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Support.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Support.Application;

/// <summary>Where new demo requests are announced. No address means they only show in the admin panel.</summary>
public sealed record SupportNotifications(string? DemoRequestsEmail);

public sealed record DemoRequestDto(Guid Id, string Name, string Company, string Email, string? Phone, int? Sites, string? Message, string Language,
    DemoRequestStatus Status, string? StaffNote, DateTimeOffset CreatedAt, DateTimeOffset? HandledAt);

/// <param name="Website">A field real visitors never see. Bots that fill every input fill this one too.</param>
public sealed record SubmitDemoRequestCommand(string Name, string Company, string Email, string? Phone, int? Sites, string? Message, string Language,
    string? Website) : ICommand<Result>;
public sealed record ListDemoRequestsQuery(DemoRequestStatus? Status, int Page, int PageSize) : IQuery<Result<PagedResult<DemoRequestDto>>>;
public sealed record HandleDemoRequestCommand(Guid DemoRequestId, DemoRequestStatus Status, string? Note) : ICommand<Result<DemoRequestDto>>, IAuditedCommand
{
    public string AuditAction => "support.handle-demo-request";
    public string AuditTarget => $"demo-request:{DemoRequestId}";
    public string? AuditDetail => Status.ToString();
}

internal sealed class SubmitDemoRequestValidator : AbstractValidator<SubmitDemoRequestCommand>
{
    public SubmitDemoRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DemoRequest.MaxName).Must(OneLine);
        RuleFor(x => x.Company).NotEmpty().MaximumLength(DemoRequest.MaxCompany).Must(OneLine);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(DemoRequest.MaxEmail).EmailAddress().Must(OneLine);
        RuleFor(x => x.Phone).MaximumLength(DemoRequest.MaxPhone).Matches(@"^[0-9+()\-\s]*$");
        RuleFor(x => x.Sites).InclusiveBetween(1, DemoRequest.MaxSites);
        RuleFor(x => x.Message).MaximumLength(DemoRequest.MaxMessage);
        RuleFor(x => x.Language).Must(l => l is "ar" or "en");
    }

    // These go into an email subject and body lines, so a line break could fake extra headers or lines.
    private static bool OneLine(string? value) => value is null || !value.Any(char.IsControl);
}

internal sealed class HandleDemoRequestValidator : AbstractValidator<HandleDemoRequestCommand>
{
    public HandleDemoRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(DemoRequest.MaxNote);
    }
}

internal sealed class DemoRequestPageValidator : AbstractValidator<ListDemoRequestsQuery>
{
    public DemoRequestPageValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>
/// Saves the request and tells sales. The answer is the same whether the request was saved, caught by the hidden field,
/// or repeated, so the form can't be used to learn anything.
/// </summary>
internal sealed partial class SubmitDemoRequestHandler(ISupportStore store, IEmailSender email, SupportNotifications notifications, TimeProvider clock,
    ILogger<SubmitDemoRequestHandler> logger) : ICommandHandler<SubmitDemoRequestCommand, Result>
{
    /// <summary>One open request per address a day is enough; more are someone pressing the button again, or a flood.</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromDays(1);

    public async Task<Result> HandleAsync(SubmitDemoRequestCommand command, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(command.Website)) return Result.Success();
        var now = clock.GetUtcNow();
        if (await store.HasOpenDemoRequestAsync(command.Email.Trim(), now - RepeatWindow, ct)) return Result.Success();

        var request = DemoRequest.Submit(command.Name, command.Company, command.Email, command.Phone, command.Sites, command.Message, command.Language, now);
        store.Add(request);
        await store.SaveAsync(ct);

        if (notifications.DemoRequestsEmail is { Length: > 0 } to)
        {
            // Saved either way; the email is a heads-up and the request waits in the admin panel.
            try
            {
                await email.SendAsync(SupportEmails.DemoRequested(to, request), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                EmailFailed(logger, ex, request.Id);
            }
        }
        return Result.Success();
    }

    [LoggerMessage(LogLevel.Warning, "Could not email demo request {DemoRequestId} to sales")]
    private static partial void EmailFailed(ILogger logger, Exception ex, Guid demoRequestId);
}

internal sealed class ListDemoRequestsHandler(SupportScope scope, ISupportStore store)
    : IQueryHandler<ListDemoRequestsQuery, Result<PagedResult<DemoRequestDto>>>
{
    public async Task<Result<PagedResult<DemoRequestDto>>> HandleAsync(ListDemoRequestsQuery query, CancellationToken ct)
    {
        if (!scope.IsStaff) return SupportErrors.StaffOnly;
        var page = await store.DemoRequestsAsync(query.Status, query.Page, query.PageSize, ct);
        return new PagedResult<DemoRequestDto>(page.Items.Select(DemoRequestViews.Dto).ToList(), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class HandleDemoRequestHandler(SupportScope scope, ISupportStore store, TimeProvider clock)
    : ICommandHandler<HandleDemoRequestCommand, Result<DemoRequestDto>>
{
    public async Task<Result<DemoRequestDto>> HandleAsync(HandleDemoRequestCommand command, CancellationToken ct)
    {
        if (!scope.IsStaff) return SupportErrors.StaffOnly;
        if (await store.DemoRequestAsync(command.DemoRequestId, ct) is not { } request) return SupportErrors.DemoRequestNotFound;
        request.Handle(command.Status, command.Note, clock.GetUtcNow());
        await store.SaveAsync(ct);
        return DemoRequestViews.Dto(request);
    }
}

internal static class DemoRequestViews
{
    public static DemoRequestDto Dto(DemoRequest r) =>
        new(r.Id, r.Name, r.Company, r.Email, r.Phone, r.Sites, r.Message, r.Language, r.Status, r.StaffNote, r.CreatedAt, r.HandledAt);
}
