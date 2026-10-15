namespace MonitorAgent.BuildingBlocks.Application;

/// <summary>The signed-in caller, as the host's authentication sees it. Implemented by the host.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated => UserId is not null;
    bool IsInRole(string role);
}

/// <summary>Builds links into the customer portal for emails. The base URL comes from <c>Portal:Url</c>.</summary>
public sealed class PortalLinks(string baseUrl)
{
    public string To(string pathAndQuery) => baseUrl.TrimEnd('/') + "/" + pathAndQuery.TrimStart('/');
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
