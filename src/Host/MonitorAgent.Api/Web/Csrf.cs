using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MonitorAgent.BuildingBlocks.Web;

namespace MonitorAgent.Api.Web;

internal static class Csrf
{
    /// <summary>Angular's HttpClient copies the XSRF-TOKEN cookie into this header on every unsafe same-origin request.</summary>
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string CookieName = "XSRF-TOKEN";
}

/// <summary>
/// Issues the CSRF token pair. The SPA calls this on start and again after sign-in or sign-out, because the token is
/// bound to the signed-in user.
/// </summary>
[ApiController]
[Route("api/v1/auth/csrf")]
[AllowAnonymous]
public sealed class CsrfController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Get()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        // Readable by the SPA's script on purpose: the protection is that other sites can't read it.
        Response.Cookies.Append(Csrf.CookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
        return NoContent();
    }
}

/// <summary>
/// Rejects any request with an unsafe method whose CSRF header doesn't match the antiforgery cookie. A plain API
/// version of MVC's AutoValidateAntiforgeryToken, which needs the Razor view services.
/// </summary>
internal sealed class ValidateCsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method)
            || HttpMethods.IsOptions(context.HttpContext.Request.Method) || HttpMethods.IsTrace(context.HttpContext.Request.Method))
            return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<NoCsrfAttribute>().Any()) return;

        if (await antiforgery.IsRequestValidAsync(context.HttpContext)) return;

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The request is missing a valid CSRF token.",
            Extensions = { ["code"] = "CSRF_INVALID" },
        })
        { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { "application/problem+json" } };
    }
}
