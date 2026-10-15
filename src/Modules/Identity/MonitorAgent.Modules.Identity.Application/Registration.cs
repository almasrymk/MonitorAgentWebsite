using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Identity.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Identity.Application;

/// <summary>
/// Creates a customer account and emails a confirmation link. The response is the same whether or not the email is
/// already registered, so the endpoint can't be used to discover accounts; the existing owner gets a notice instead.
/// </summary>
public sealed record RegisterCommand(string Email, string Password, string FullName) : ICommand<Result>;

public sealed record ConfirmEmailCommand(Guid UserId, string Token) : ICommand<Result>;

/// <summary>Sends a new confirmation link to an unconfirmed account. Always succeeds, for the same reason as registration.</summary>
public sealed record ResendConfirmationCommand(string Email) : ICommand<Result>;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(128);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
    }
}

internal sealed class ConfirmEmailValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
    }
}

internal sealed class ResendConfirmationValidator : AbstractValidator<ResendConfirmationCommand>
{
    public ResendConfirmationValidator() => RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
}

internal sealed class RegisterHandler(IUserAccounts users, IEmailSender email, IIdentityLinks links, ISecurityLog log)
    : ICommandHandler<RegisterCommand, Result>
{
    public async Task<Result> HandleAsync(RegisterCommand command, CancellationToken ct)
    {
        var existing = await users.FindByEmailAsync(command.Email);
        if (existing is not null)
        {
            await email.SendAsync(Emails.AlreadyRegistered(existing.Email), ct);
            return Result.Success();
        }

        var created = await users.CreateAsync(command.Email.Trim(), command.FullName.Trim(), command.Password, IdentityRoles.Customer);
        if (created.IsFailure) return created.Error!;

        var user = created.Value;
        var token = await users.CreateEmailConfirmationTokenAsync(user.Id);
        await email.SendAsync(Emails.ConfirmEmail(user.Email, user.FullName, links.ConfirmEmail(user.Id, token)), ct);
        await log.RecordAsync(user.Id, SecurityEventType.Registered, ct);
        return Result.Success();
    }
}

internal sealed class ConfirmEmailHandler(IUserAccounts users, ISecurityLog log) : ICommandHandler<ConfirmEmailCommand, Result>
{
    public async Task<Result> HandleAsync(ConfirmEmailCommand command, CancellationToken ct)
    {
        if (!await users.ConfirmEmailAsync(command.UserId, command.Token)) return IdentityErrors.InvalidToken;
        await log.RecordAsync(command.UserId, SecurityEventType.EmailConfirmed, ct);
        return Result.Success();
    }
}

internal sealed class ResendConfirmationHandler(IUserAccounts users, IEmailSender email, IIdentityLinks links)
    : ICommandHandler<ResendConfirmationCommand, Result>
{
    public async Task<Result> HandleAsync(ResendConfirmationCommand command, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(command.Email);
        if (user is null || user.EmailConfirmed) return Result.Success();
        var token = await users.CreateEmailConfirmationTokenAsync(user.Id);
        await email.SendAsync(Emails.ConfirmEmail(user.Email, user.FullName, links.ConfirmEmail(user.Id, token)), ct);
        return Result.Success();
    }
}
