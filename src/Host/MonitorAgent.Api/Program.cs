using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.Api.Web;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Audit.Infrastructure;
using MonitorAgent.Modules.Commerce.Infrastructure;
using MonitorAgent.Modules.Content.Infrastructure;
using MonitorAgent.Modules.Customers.Infrastructure;
using MonitorAgent.Modules.Identity.Infrastructure;
using MonitorAgent.Modules.Licenses.Infrastructure;
using MonitorAgent.Modules.Releases.Infrastructure;
using MonitorAgent.Modules.Support.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Every unsafe request must carry the CSRF header that matches the antiforgery cookie (ADR-005).
builder.Services.AddControllers(o => o.Filters.Add<ValidateCsrfFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddCqrs();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddEmailSender(builder.Configuration);
var portalUrl = builder.Configuration["Portal:Url"];
if (string.IsNullOrWhiteSpace(portalUrl))
    throw new InvalidOperationException("Set Portal:Url. Account and invitation emails link into the customer portal.");
builder.Services.AddSingleton(new PortalLinks(portalUrl));

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddCustomersModule(builder.Configuration);
builder.Services.AddLicensesModule(builder.Configuration, builder.Environment);
builder.Services.AddCommerceModule(builder.Configuration, builder.Environment);
builder.Services.AddReleasesModule(builder.Configuration);
builder.Services.AddSupportModule(builder.Configuration);
builder.Services.AddContentModule(builder.Configuration);
builder.Services.AddAuditModule(builder.Configuration);

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

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).DisableRateLimiting();
app.MapControllers();

await app.RunAsync();

static RateLimitPartition<string> PerClientIp(HttpContext http, int permitPerMinute) =>
    RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1) });

public partial class Program;
