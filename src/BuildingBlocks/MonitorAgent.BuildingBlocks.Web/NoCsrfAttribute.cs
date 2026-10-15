namespace MonitorAgent.BuildingBlocks.Web;

/// <summary>
/// Exempts an endpoint from the CSRF header check. Only for server-to-server callbacks (payment webhooks) that carry
/// no cookies and prove who sent them with a signature the handler verifies.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoCsrfAttribute : Attribute;
