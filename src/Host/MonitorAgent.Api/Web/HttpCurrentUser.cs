using System.Security.Claims;
using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Api.Web;

internal sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid? UserId =>
        http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id && Guid.TryParse(id, out var userId) ? userId : null;

    public bool IsInRole(string role) => http.HttpContext?.User.IsInRole(role) ?? false;
}
