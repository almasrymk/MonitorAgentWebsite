using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Customers.Domain;

/// <summary>Error codes the Customers endpoints return. They are part of the API contract.</summary>
public static class CustomersErrors
{
    public static readonly Error NoCompany = Error.NotFound("NO_COMPANY", "You are not part of a company account yet.");
    public static readonly Error AlreadyInCompany = Error.Conflict("ALREADY_IN_COMPANY", "You already belong to a company account.");
    public static readonly Error NotOwner = Error.Forbidden("NOT_COMPANY_OWNER", "Only an owner of the company account can do this.");
    public static readonly Error MemberNotFound = Error.NotFound("MEMBER_NOT_FOUND", "No such member in your company.");
    public static readonly Error InvitationNotFound = Error.NotFound("INVITATION_NOT_FOUND", "No such invitation in your company.");
    public static readonly Error LastOwner = Error.Conflict("LAST_OWNER", "A company needs at least one owner. Make someone else an owner first.");
    public static readonly Error AlreadyMember = Error.Conflict("ALREADY_MEMBER", "This person is already a member.");
    public static readonly Error AlreadyInvited = Error.Conflict("ALREADY_INVITED", "There is already a pending invitation for this email.");
    public static readonly Error TeamFull = Error.Conflict("TEAM_FULL", "The company has reached its member limit.");
    // One error for a wrong, expired, used or revoked token, so tokens can't be probed.
    public static readonly Error InvalidInvitation = Error.Validation("INVALID_INVITATION", "This invitation is invalid or has expired.");
    public static readonly Error InvitationForAnotherEmail = Error.Forbidden("INVITATION_EMAIL_MISMATCH", "This invitation was sent to a different email address.");
}
