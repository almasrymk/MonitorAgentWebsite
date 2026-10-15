using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Api.Web;

/// <summary>Development only: writes emails to the log so links can be copied. Never used outside Development.</summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogInformation("Email to {To}: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}

internal static class EmailSenders
{
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var provider = configuration["Email:Provider"];
        switch (provider)
        {
            case "Log" when environment.IsDevelopment():
                services.AddSingleton<IEmailSender, LoggingEmailSender>();
                break;
            case "Log":
                throw new InvalidOperationException("Email:Provider 'Log' prints account links to the log and is only allowed in Development.");
            case null or "":
                throw new InvalidOperationException("Set Email:Provider. Account confirmation and password reset need email.");
            default:
                throw new InvalidOperationException($"Unknown Email:Provider '{provider}'.");
        }
        return services;
    }
}
