using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.Modules.Identity.Application;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Identity.Infrastructure.Persistence;
using MonitorAgent.Modules.Identity.Infrastructure.Services;

namespace MonitorAgent.Modules.Identity.Infrastructure;

public sealed class IdentityModuleOptions
{
    /// <summary>How often a session is re-checked against the account, so "sign out everywhere" and password changes take effect.</summary>
    public int SessionValidationSeconds { get; set; } = 60;

    /// <summary>Migrate (production and development), EnsureCreated (tests) or None.</summary>
    public string DatabaseInitialization { get; set; } = "None";

    /// <summary>Creates this admin on startup when no admin exists yet. The password comes from configuration or a secret store, never the repo.</summary>
    public string? BootstrapAdminEmail { get; set; }
    public string? BootstrapAdminPassword { get; set; }
}

public static class IdentityModule
{
    public const string SessionCookie = "__Host-ma.session";
    public const string MfaCookie = "__Host-ma.mfa";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Identity").Get<IdentityModuleOptions>() ?? new IdentityModuleOptions();
        services.AddSingleton(options);

        services.AddDbContext<IdentityDb>(db => db.UseSqlServer(configuration.GetConnectionString("Default"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDb.Schema)));

        services.AddDataProtection().SetApplicationName("MonitorAgentWebsite").PersistKeysToDbContext<IdentityDb>();

        services.AddIdentity<ApplicationUser, ApplicationRole>(o =>
            {
                // NIST 800-63B: length over composition rules.
                o.Password.RequiredLength = 12;
                o.Password.RequiredUniqueChars = 4;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<IdentityDb>()
            .AddDefaultTokenProviders();

        // Email confirmation and password reset links expire after 3 hours.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(3));
        services.Configure<SecurityStampValidatorOptions>(o =>
        {
            o.ValidationInterval = TimeSpan.FromSeconds(options.SessionValidationSeconds);
            // The periodic re-check rebuilds the principal from the database; keep the proof of two-step sign-in.
            o.OnRefreshingPrincipal = context =>
            {
                if (context.CurrentPrincipal?.HasClaim("amr", "mfa") == true)
                    context.NewPrincipal?.Identities.First().AddClaim(new System.Security.Claims.Claim("amr", "mfa"));
                return Task.CompletedTask;
            };
        });

        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = SessionCookie;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.Cookie.Path = "/";
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;
            // An API answers 401/403 with ProblemDetails; it never redirects to a login page.
            cookie.Events.OnRedirectToLogin = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                "UNAUTHENTICATED", "Sign in to continue.");
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                var user = context.HttpContext.User;
                var needsMfa = IdentityRoles.Staff.Any(user.IsInRole) && !user.HasClaim("amr", "mfa");
                return needsMfa
                    ? WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "MFA_REQUIRED",
                        "Turn on two-step verification and sign in with it to use the admin area.")
                    : WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "FORBIDDEN", "You don't have access to this.");
            };
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, cookie =>
        {
            cookie.Cookie.Name = MfaCookie;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.Cookie.Path = "/";
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Customer, p => p.RequireAuthenticatedUser().RequireRole(IdentityRoles.Customer))
            .AddPolicy(AuthPolicies.Staff, p => p.RequireAuthenticatedUser().RequireRole(IdentityRoles.Staff).RequireClaim("amr", "mfa"))
            .AddPolicy(AuthPolicies.StaffAdmin, p => p.RequireAuthenticatedUser().RequireRole(IdentityRoles.Admin).RequireClaim("amr", "mfa"));

        services.AddHttpContextAccessor();
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<ISignInSession, SignInSession>();
        services.AddScoped<SecurityLog>();
        services.AddScoped<ISecurityLog>(sp => sp.GetRequiredService<SecurityLog>());
        services.AddScoped<ISecurityEventQueries>(sp => sp.GetRequiredService<SecurityLog>());
        services.AddScoped<IIdentityLinks, IdentityLinks>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddHostedService<IdentitySeeder>();

        services.AddIdentityApplication();
        services.AddControllers().AddApplicationPart(typeof(IdentityModule).Assembly);
        return services;
    }

    private static Task WriteProblemAsync(HttpContext http, int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/problem+json";
        return http.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonSerializerOptions.Web));
    }
}
