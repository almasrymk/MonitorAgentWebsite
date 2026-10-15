using System.Net;
using System.Net.Mail;
using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Api.Web;

internal sealed class SmtpOptions
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public string From { get; init; } = "";
}

/// <summary>
/// Sends through any SMTP relay. In development it points at Mailpit (docker compose), so account links are read
/// in its inbox instead of being written to logs.
/// </summary>
internal sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl };
        if (!string.IsNullOrEmpty(options.UserName))
            client.Credentials = new NetworkCredential(options.UserName, options.Password);
        using var mail = new MailMessage(options.From, message.To, message.Subject, message.TextBody);
        await client.SendMailAsync(mail, ct);
    }
}

internal static class EmailSenders
{
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Email:Provider"];
        switch (provider)
        {
            case "Smtp":
                var smtp = configuration.GetSection("Email:Smtp").Get<SmtpOptions>() ?? new SmtpOptions();
                if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.From))
                    throw new InvalidOperationException("Set Email:Smtp:Host and Email:Smtp:From.");
                services.AddSingleton(smtp);
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
                break;
            case null or "":
                throw new InvalidOperationException("Set Email:Provider. Account confirmation and password reset need email.");
            default:
                throw new InvalidOperationException($"Unknown Email:Provider '{provider}'.");
        }
        return services;
    }
}
