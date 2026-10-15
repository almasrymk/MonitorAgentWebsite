namespace MonitorAgent.Modules.Identity.Contracts;

/// <summary>Role names. Staff roles are for the company's own people; every self-registered account gets <see cref="Customer"/>.</summary>
public static class IdentityRoles
{
    public const string Customer = "Customer";

    public const string Admin = "Admin";
    public const string Support = "Support";
    public const string Finance = "Finance";
    public const string ContentEditor = "ContentEditor";
    public const string ContentReviewer = "ContentReviewer";

    public static readonly IReadOnlyList<string> Staff = [Admin, Support, Finance, ContentEditor, ContentReviewer];
    public static readonly IReadOnlyList<string> All = [Customer, .. Staff];
}

/// <summary>Authorization policy names other modules put on their endpoints.</summary>
public static class AuthPolicies
{
    /// <summary>A signed-in customer account.</summary>
    public const string Customer = "Customer";

    /// <summary>Any staff role, signed in with a second factor in this session.</summary>
    public const string Staff = "Staff";

    /// <summary>Staff with the Admin role, signed in with a second factor.</summary>
    public const string StaffAdmin = "Staff.Admin";
}
