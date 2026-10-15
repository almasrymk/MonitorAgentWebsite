using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Tests.Api;

[ApiController]
[Route("test/errors")]
public sealed class ThrowingController : ControllerBase
{
    [HttpGet("crash")]
    public IActionResult Crash() => throw new InvalidOperationException("secret internal detail");

    [HttpGet("domain")]
    public IActionResult Domain() => throw new DomainException(Error.Conflict("ORDER_ALREADY_PAID", "Order is already paid."));

    [HttpGet("validation")]
    public IActionResult Validation() => throw new ValidationException("Email is required.");
}

public class ErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddControllers().AddApplicationPart(typeof(ThrowingController).Assembly)))
        .CreateClient();

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_without_internal_details_and_with_a_trace_id()
    {
        var response = await _client.GetAsync("/test/errors/crash");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret internal detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        var problem = await ProblemAsync(response);
        Assert.Equal("INTERNAL_ERROR", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Domain_exception_returns_its_status_and_code()
    {
        var response = await _client.GetAsync("/test/errors/domain");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ORDER_ALREADY_PAID", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Validation_exception_returns_400()
    {
        var response = await _client.GetAsync("/test/errors/validation");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await ProblemAsync(response)).GetProperty("code").GetString());
    }
}
