using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Customers.Application;
using MonitorAgent.Modules.Customers.Domain;
using MonitorAgent.Modules.Identity.Contracts;

namespace MonitorAgent.Modules.Customers.Infrastructure.Http;

public sealed record CompanyNameRequest(string Name);
public sealed record InviteRequest(string Email, MemberRole Role);
public sealed record AcceptInvitationRequest(string Token);
public sealed record RoleRequest(MemberRole Role);

/// <summary>
/// The signed-in customer's company. No route takes a company id: the company is always the caller's own,
/// found through their membership, so these endpoints can't reach another tenant.
/// </summary>
[ApiController]
[Route("api/v1/company")]
[Authorize(Policy = AuthPolicies.Customer)]
public sealed class CompanyController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CompanyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => (await dispatcher.QueryAsync(new GetMyCompanyQuery(), ct)).ToActionResult();

    [HttpPost]
    [ProducesResponseType<CompanyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CompanyNameRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new CreateCompanyCommand(request.Name), ct)).ToActionResult();

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Rename(CompanyNameRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new RenameCompanyCommand(request.Name), ct)).ToActionResult();

    [HttpPost("invitations")]
    [ProducesResponseType<InvitationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invite(InviteRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new InviteMemberCommand(request.Email, request.Role), ct)).ToActionResult();

    [HttpDelete("invitations/{invitationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeInvitation(Guid invitationId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new RevokeInvitationCommand(invitationId), ct)).ToActionResult();

    [HttpPost("invitations/accept")]
    [ProducesResponseType<CompanyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvitation(AcceptInvitationRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new AcceptInvitationCommand(request.Token), ct)).ToActionResult();

    [HttpPut("members/{userId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeRole(Guid userId, RoleRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ChangeMemberRoleCommand(userId, request.Role), ct)).ToActionResult();

    [HttpDelete("members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(Guid userId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new RemoveMemberCommand(userId), ct)).ToActionResult();
}
