using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Licenses.Domain;

/// <summary>Error codes the Licenses endpoints return. They are part of the API contract.</summary>
public static class LicensesErrors
{
    public static readonly Error PlatformUnavailable = Error.Unavailable("LICENSING_UNAVAILABLE", "The licensing service can't be reached right now. Try again shortly.");
    public static readonly Error MonitorUnavailable = Error.Unavailable("MONITOR_UNAVAILABLE", "The monitoring platform can't be reached right now. Try again shortly.");
    public static readonly Error LicenseNotFound = Error.NotFound("LICENSE_NOT_FOUND", "No such license in your company.");
    public static readonly Error SubscriptionNotFound = Error.NotFound("SUBSCRIPTION_NOT_FOUND", "No such subscription in your company.");
    public static readonly Error DeviceNotFound = Error.NotFound("DEVICE_NOT_FOUND", "No such device on this license.");
    public static readonly Error PlanNotAvailable = Error.Validation("PLAN_NOT_AVAILABLE", "This plan isn't on sale.");
    public static readonly Error CompanyNotFound = Error.NotFound("COMPANY_NOT_FOUND", "No such company.");
    public static readonly Error NoCompany = Error.NotFound("NO_COMPANY", "You are not part of a company account yet.");
    public static readonly Error NotAllowed = Error.Forbidden("NOT_ALLOWED_FOR_ROLE", "Only company owners and technical members can do this.");
    public static readonly Error KeyNotAvailable = Error.NotFound("PRODUCT_KEY_NOT_AVAILABLE", "This license's key wasn't issued through the website.");
    /// <summary>INT-016: another request is issuing this order's license right now. Retry shortly.</summary>
    public static readonly Error IssueInProgress = Error.Unavailable("LICENSE_ISSUE_IN_PROGRESS", "This license is being issued right now. Try again shortly.");
    public static readonly Error NotRenewable = Error.Conflict("LICENSE_NOT_RENEWABLE", "This license can't be renewed. A revoked license needs a new order.");
    /// <summary>INT-014: moving to a dearer plan in the middle of a term needs a paid upgrade order.</summary>
    public static readonly Error UpgradeRequiresPayment = Error.Conflict("PLAN_UPGRADE_REQUIRES_PAYMENT", "Moving to a higher plan needs a paid upgrade. A cheaper plan can be scheduled for your next renewal.");
    /// <summary>INT-014: only a dearer plan of the same product, in the same currency, is an upgrade.</summary>
    public static readonly Error NotAnUpgrade = Error.Validation("UPGRADE_NOT_HIGHER", "Only a higher-priced plan of the same product, in the same currency, can be bought as an upgrade. A cheaper plan can be scheduled for your next renewal.");
    /// <summary>INT-014: less than one full day is left, so the upgrade is bought with the renewal instead.</summary>
    public static readonly Error UpgradeTermEnding = Error.Conflict("UPGRADE_TERM_ENDING", "Your term ends within a day. Schedule the plan and renew instead.");
    /// <summary>INT-014: a perpetual plan, a plan no longer on sale or a subscription that is not active can't be prorated.</summary>
    public static readonly Error NotUpgradable = Error.Conflict("UPGRADE_NOT_AVAILABLE", "This subscription can't be upgraded online. Contact us and we will change it for you.");
    public static readonly Error SamePlan = Error.Conflict("SUBSCRIPTION_SAME_PLAN", "Subscription is already on this plan.");
    /// <summary>B5: this account already started its trial (one per account and product).</summary>
    public static readonly Error TrialAlreadyUsed = Error.Conflict("TRIAL_ALREADY_USED", "You have already used your free trial. Buy a plan to keep monitoring.");
    public static readonly Error TrialUnavailable = Error.NotFound("TRIAL_UNAVAILABLE", "A free trial isn't offered right now.");
    public static readonly Error OrderMismatch = Error.Conflict("ORDER_REFERENCE_REUSED", "This order reference was already used for a different company or plan.");
}
