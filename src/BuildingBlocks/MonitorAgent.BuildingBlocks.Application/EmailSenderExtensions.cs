using Microsoft.Extensions.Logging;

namespace MonitorAgent.BuildingBlocks.Application;

public static partial class EmailSenderExtensions
{
    /// <summary>
    /// Sends an email that follows a change already saved (a new account, a changed password). If the mail server is down
    /// the change still stands, so the failure is logged for operations and the caller carries on instead of answering 500.
    /// </summary>
    public static async Task<bool> TrySendAsync(this IEmailSender sender, EmailMessage message, ILogger logger, CancellationToken ct)
    {
        try
        {
            await sender.SendAsync(message, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EmailFailed(logger, ex, message.Subject);
            return false;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Could not send the email \"{Subject}\"")]
    private static partial void EmailFailed(ILogger logger, Exception ex, string subject);
}
