using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MonitorAgent.Tests.Unit;

public class ResultTests
{
    [Fact]
    public void Success_carries_its_value_and_failure_hides_it()
    {
        Result<int> ok = 5;
        Result<int> failed = Error.NotFound("ORDER_NOT_FOUND", "Order not found.");

        Assert.Equal(5, ok.Value);
        Assert.True(failed.IsFailure);
        Assert.Throws<InvalidOperationException>(() => failed.Value);
    }

    [Fact]
    public void Guard_trims_and_rejects_blank_or_long_values()
    {
        Assert.Equal("abc", Guard.NotEmpty("  abc ", "Name"));
        Assert.Equal("VALIDATION_FAILED", Assert.Throws<DomainException>(() => Guard.NotEmpty(" ", "Name")).Error.Code);
        Assert.Throws<DomainException>(() => Guard.NotEmpty(new string('x', 201), "Name"));
    }

    [Theory]
    [InlineData(ErrorKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorKind.TooManyRequests, StatusCodes.Status429TooManyRequests)]
    public void Failures_map_to_the_matching_status_with_the_error_code(ErrorKind kind, int status)
    {
        var result = Result.Failure(new Error("SOME_CODE", "Message.", kind)).ToActionResult();

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal("SOME_CODE", problem.Extensions["code"]);
    }

    [Fact]
    public void Success_maps_to_200_with_the_value_or_204_without_one()
    {
        Assert.Equal(42, Assert.IsType<OkObjectResult>(Result.Success(42).ToActionResult()).Value);
        Assert.IsType<NoContentResult>(Result.Success().ToActionResult());
    }
}
