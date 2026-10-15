using Microsoft.AspNetCore.Mvc;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Api.Web;

/// <summary>Maps <see cref="Result"/> failures to HTTP. The error code travels in <c>code</c> and is part of the API contract.</summary>
public static class ResultMapping
{
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Gone => StatusCodes.Status410Gone,
        ErrorKind.Locked => StatusCodes.Status423Locked,
        ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest,
    };

    public static ProblemDetails ToProblem(Error error)
    {
        var problem = new ProblemDetails { Status = StatusFor(error.Kind), Title = error.Message };
        problem.Extensions["code"] = error.Code;
        return problem;
    }

    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new NoContentResult() : Problem(result.Error!);

    public static IActionResult ToActionResult<T>(this Result<T> result) =>
        result.IsSuccess ? new OkObjectResult(result.Value) : Problem(result.Error!);

    private static ObjectResult Problem(Error error)
    {
        var problem = ToProblem(error);
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }
}
