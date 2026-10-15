using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.Api.Web;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Every unsafe request must carry the CSRF header that matches the antiforgery cookie (ADR-005).
builder.Services.AddControllers(o => o.Filters.Add<ValidateCsrfFilter>());
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = Csrf.HeaderName;
    o.Cookie.Name = "__Host-ma.af";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddCqrs();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddEmailSender(builder.Configuration);

builder.Services.AddIdentityModule(builder.Configuration);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        PerClientIp(http, builder.Configuration.GetValue("RateLimiting:PermitPerMinute", 300)));
    options.AddPolicy(RateLimitPolicies.Auth, http =>
        PerClientIp(http, builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 10)));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live").DisableRateLimiting();
app.MapHealthChecks("/health/ready").DisableRateLimiting();
app.MapControllers();

await app.RunAsync();

static RateLimitPartition<string> PerClientIp(HttpContext http, int permitPerMinute) =>
    RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1) });

public partial class Program;
