using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Api.Web;

/// <summary>
/// Turns domain and validation exceptions into ProblemDetails with a stable error code. Anything else becomes a
/// 500 without internal details, so stack traces and messages never reach the client.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            DomainException domain => ResultMapping.ToProblem(domain.Error),
            ValidationException validation => ResultMapping.ToProblem(Error.Validation("VALIDATION_FAILED",
                string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()))),
            _ => null,
        };
        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
            problem = new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "An unexpected error occurred." };
            problem.Extensions["code"] = "INTERNAL_ERROR";
        }

        http.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }
}
