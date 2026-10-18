using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.BuildingBlocks.Web;
using MonitorAgent.Modules.Identity.Contracts;
using MonitorAgent.Modules.Releases.Application;
using MonitorAgent.Modules.Releases.Domain;

namespace MonitorAgent.Modules.Releases.Infrastructure.Http;

public sealed record AddReleaseRequest(string ManifestBase64, string Signature, string Channel = Release.StableChannel);

/// <summary>
/// What the Monitor Agent program reads (its Updates:ManifestUrl): the signed manifest of the newest published version, byte
/// for byte as the publisher signed it, and its signature at the same address plus ".sig".
/// </summary>
[ApiController]
[Route("updates")]
[AllowAnonymous]
public sealed class UpdateManifestController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("manifest.json")]
    public Task<IActionResult> Manifest(CancellationToken ct) => ManifestAsync(Release.StableChannel, ct);

    [HttpGet("manifest.json.sig")]
    public Task<IActionResult> Signature(CancellationToken ct) => SignatureAsync(Release.StableChannel, ct);

    [HttpGet("{channel:regex(^[[a-z]][[a-z0-9-]]*$)}/manifest.json")]
    public Task<IActionResult> ChannelManifest(string channel, CancellationToken ct) => ManifestAsync(channel, ct);

    [HttpGet("{channel:regex(^[[a-z]][[a-z0-9-]]*$)}/manifest.json.sig")]
    public Task<IActionResult> ChannelSignature(string channel, CancellationToken ct) => SignatureAsync(channel, ct);

    private async Task<IActionResult> ManifestAsync(string channel, CancellationToken ct)
    {
        var current = await dispatcher.QueryAsync(new CurrentManifestQuery(channel), ct);
        if (current.IsFailure) return current.ToActionResult();
        ShortCache();
        return File(current.Value.Manifest, "application/json");
    }

    private async Task<IActionResult> SignatureAsync(string channel, CancellationToken ct)
    {
        var current = await dispatcher.QueryAsync(new CurrentManifestQuery(channel), ct);
        if (current.IsFailure) return current.ToActionResult();
        ShortCache();
        return File(Encoding.ASCII.GetBytes(current.Value.Signature), "text/plain");
    }

    // Programs check every few hours; five minutes keeps a withdrawn version from being offered for long.
    private void ShortCache() => Response.Headers.CacheControl = "public, max-age=300";
}

/// <summary>Published versions, for the website's download and changelog pages.</summary>
[ApiController]
[Route("api/v1/releases")]
[AllowAnonymous]
public sealed class ReleasesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReleaseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => (await dispatcher.QueryAsync(new ListReleasesQuery(PublishedOnly: true), ct)).ToActionResult();
}

/// <summary>Adding, publishing and withdrawing versions. Manifests are signed offline; the website only checks them.</summary>
[ApiController]
[Route("api/v1/admin/releases")]
[Authorize(Policy = AuthPolicies.StaffAdmin)]
public sealed class AdminReleasesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReleaseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => (await dispatcher.QueryAsync(new ListReleasesQuery(PublishedOnly: false), ct)).ToActionResult();

    [HttpPost]
    [ProducesResponseType<ReleaseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Add(AddReleaseRequest request, CancellationToken ct) =>
        (await dispatcher.SendAsync(new AddReleaseCommand(request.Channel, request.ManifestBase64, request.Signature), ct)).ToActionResult();

    [HttpPost("{releaseId:guid}/publish")]
    [ProducesResponseType<ReleaseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Publish(Guid releaseId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new PublishReleaseCommand(releaseId), ct)).ToActionResult();

    [HttpPost("{releaseId:guid}/withdraw")]
    [ProducesResponseType<ReleaseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw(Guid releaseId, CancellationToken ct) =>
        (await dispatcher.SendAsync(new WithdrawReleaseCommand(releaseId), ct)).ToActionResult();
}
