using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MonitorAgent.BuildingBlocks.Web;

/// <summary>
/// The <c>X-Correlation-Id</c> header that the Website, Monitor Cloud and LicensingPlatform share, so that one customer
/// action can be followed through the logs of all three. An incoming id is kept when it is a short, safe token;
/// otherwise a new one is created. The id is echoed on the response and added to the logging scope.
/// </summary>
public static partial class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    /// <summary>The id of the current request, or <c>null</c> outside a request (background workers).</summary>
    public static string? Of(HttpContext? context) => context?.Items[ItemKey] as string;

    /// <summary>Only short identifiers made of safe characters are accepted (no header injection, no log forging).</summary>
    public static bool IsValid(string? value) => value is { Length: > 0 and <= 64 } && SafeId().IsMatch(value);

    public static string New() => Guid.CreateVersion7().ToString("N");

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var id = IsValid(incoming) ? incoming : New();
        context.Items[ItemKey] = id;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("MonitorAgent.Correlation");
        using (logger.BeginScope(new Dictionary<string, object> { [ItemKey] = id }))
            await next(context);
    });

    /// <summary>Adds the forwarding handler to an outgoing HTTP client (LicensingPlatform, Monitor Cloud).</summary>
    public static IHttpClientBuilder AddCorrelationId(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdHandler>();
        return builder.AddHttpMessageHandler<CorrelationIdHandler>();
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$")]
    private static partial Regex SafeId();
}

/// <summary>
/// Forwards the current request's correlation id on outgoing calls. A call made outside a request (an outbox or
/// reminder worker) gets a new id, so the other service can still tie its log lines together.
/// </summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Headers.Contains(CorrelationId.HeaderName))
            request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName,
                CorrelationId.Of(accessor.HttpContext) ?? CorrelationId.New());
        return base.SendAsync(request, cancellationToken);
    }
}
