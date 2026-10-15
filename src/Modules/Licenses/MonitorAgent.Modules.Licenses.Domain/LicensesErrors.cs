using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Domain;

/// <summary>Error codes the Licenses endpoints return. They are part of the API contract.</summary>
public static class LicensesErrors
{
    public static readonly Error PlatformUnavailable = Error.Unavailable("LICENSING_UNAVAILABLE", "The licensing service can't be reached right now. Try again shortly.");
    public static readonly Error LicenseNotFound = Error.NotFound("LICENSE_NOT_FOUND", "No such license in your company.");
    public static readonly Error DeviceNotFound = Error.NotFound("DEVICE_NOT_FOUND", "No such device on this license.");
    public static readonly Error PlanNotAvailable = Error.Validation("PLAN_NOT_AVAILABLE", "This plan isn't on sale.");
    public static readonly Error CompanyNotFound = Error.NotFound("COMPANY_NOT_FOUND", "No such company.");
    public static readonly Error NoCompany = Error.NotFound("NO_COMPANY", "You are not part of a company account yet.");
    public static readonly Error NotAllowed = Error.Forbidden("NOT_ALLOWED_FOR_ROLE", "Only company owners and technical members can do this.");
    public static readonly Error KeyNotAvailable = Error.NotFound("PRODUCT_KEY_NOT_AVAILABLE", "This license's key wasn't issued through the website.");
    public static readonly Error OrderMismatch = Error.Conflict("ORDER_REFERENCE_REUSED", "This order reference was already used for a different company or plan.");
}
