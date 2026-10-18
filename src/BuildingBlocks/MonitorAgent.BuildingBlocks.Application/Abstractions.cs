namespace MonitorAgent.BuildingBlocks.Application;

/// <summary>The signed-in caller, as the host's authentication sees it. Implemented by the host.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated => UserId is not null;
    bool IsInRole(string role);
}

public sealed record EmailMessage(string To, string Subject, string TextBody);

/// <summary>Sends transactional email. The host picks the implementation from configuration.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Names of the host's rate-limit policies, for endpoints in any module.</summary>
public static class RateLimitPolicies
{
    /// <summary>Sign-in, registration and recovery endpoints: a small budget per client IP.</summary>
    public const string Auth = "auth";
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
