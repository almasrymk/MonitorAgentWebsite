using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Releases.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Releases.Application;

public interface IReleaseStore
{
    Task<Release?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(string channel, int major, int minor, int patch, CancellationToken ct);
    /// <summary>The newest published release on the channel: the one programs are offered.</summary>
    Task<Release?> CurrentAsync(string channel, CancellationToken ct);
    Task<IReadOnlyList<Release>> ListAsync(bool publishedOnly, CancellationToken ct);
    void Add(Release release);
    Task SaveAsync(CancellationToken ct);
}

public sealed record PackageDto(string Platform, string Url, string Sha256, long Size);
public sealed record ReleaseDto(Guid Id, string Version, string Channel, string? Notes, ReleaseStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt, IReadOnlyList<PackageDto> Packages);
public sealed record SignedManifest(byte[] Manifest, string Signature);

/// <summary>Adds a draft from a manifest signed offline. <c>ManifestBase64</c> is the file's exact bytes.</summary>
public sealed record AddReleaseCommand(string Channel, string ManifestBase64, string Signature) : ICommand<Result<ReleaseDto>>;
public sealed record PublishReleaseCommand(Guid ReleaseId) : ICommand<Result<ReleaseDto>>;
public sealed record WithdrawReleaseCommand(Guid ReleaseId) : ICommand<Result<ReleaseDto>>;
public sealed record ListReleasesQuery(bool PublishedOnly) : IQuery<Result<IReadOnlyList<ReleaseDto>>>;
public sealed record CurrentManifestQuery(string Channel) : IQuery<Result<SignedManifest>>;

internal sealed class AddReleaseValidator : AbstractValidator<AddReleaseCommand>
{
    public AddReleaseValidator()
    {
        RuleFor(x => x.Channel).NotEmpty().MaximumLength(20).Matches("^[a-z][a-z0-9-]*$");
        RuleFor(x => x.ManifestBase64).NotEmpty().MaximumLength(ManifestReader.MaxBytes * 4 / 3 + 4);
        RuleFor(x => x.Signature).NotEmpty().MaximumLength(512);
    }
}

internal static class ReleaseMapping
{
    public static ReleaseDto ToDto(this Release r) => new(r.Id, r.Version, r.Channel, r.Notes, r.Status, r.CreatedAt, r.PublishedAt,
        r.Packages.Select(p => new PackageDto(p.Platform, p.Url, p.Sha256, p.Size)).ToList());
}

internal sealed class AddReleaseHandler(IReleaseStore store, ReleasesOptions options, TimeProvider clock)
    : ICommandHandler<AddReleaseCommand, Result<ReleaseDto>>
{
    public async Task<Result<ReleaseDto>> HandleAsync(AddReleaseCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.PublicKey)) return ReleasesErrors.KeyNotConfigured;
        byte[] manifest;
        try
        {
            manifest = Convert.FromBase64String(command.ManifestBase64);
        }
        catch (FormatException)
        {
            return ReleasesErrors.InvalidManifest("The manifest must be sent as base64.");
        }
        if (manifest.Length > ManifestReader.MaxBytes) return ReleasesErrors.InvalidManifest("The manifest is too large.");

        // The signature is checked first: nothing in an unsigned file is trusted, not even its version.
        if (!ManifestReader.SignatureIsValid(manifest, command.Signature, options.PublicKey)) return ReleasesErrors.InvalidSignature;
        var parsed = ManifestReader.Parse(manifest);
        if (parsed.IsFailure) return parsed.Error!;

        var release = Release.Draft(command.Channel, parsed.Value.Version, parsed.Value.Notes, parsed.Value.Packages, manifest,
            command.Signature.Trim(), clock.GetUtcNow());
        if (await store.ExistsAsync(release.Channel, release.Major, release.Minor, release.Patch, ct)) return ReleasesErrors.Exists;
        store.Add(release);
        await store.SaveAsync(ct);
        return release.ToDto();
    }
}

internal sealed class PublishReleaseHandler(IReleaseStore store, TimeProvider clock) : ICommandHandler<PublishReleaseCommand, Result<ReleaseDto>>
{
    public async Task<Result<ReleaseDto>> HandleAsync(PublishReleaseCommand command, CancellationToken ct)
    {
        if (await store.FindAsync(command.ReleaseId, ct) is not { } release) return ReleasesErrors.NotFound;
        var published = release.Publish(await store.CurrentAsync(release.Channel, ct), clock.GetUtcNow());
        if (published.IsFailure) return published.Error!;
        await store.SaveAsync(ct);
        return release.ToDto();
    }
}

internal sealed class WithdrawReleaseHandler(IReleaseStore store) : ICommandHandler<WithdrawReleaseCommand, Result<ReleaseDto>>
{
    public async Task<Result<ReleaseDto>> HandleAsync(WithdrawReleaseCommand command, CancellationToken ct)
    {
        if (await store.FindAsync(command.ReleaseId, ct) is not { } release) return ReleasesErrors.NotFound;
        var withdrawn = release.Withdraw();
        if (withdrawn.IsFailure) return withdrawn.Error!;
        await store.SaveAsync(ct);
        return release.ToDto();
    }
}

internal sealed class ListReleasesHandler(IReleaseStore store) : IQueryHandler<ListReleasesQuery, Result<IReadOnlyList<ReleaseDto>>>
{
    public async Task<Result<IReadOnlyList<ReleaseDto>>> HandleAsync(ListReleasesQuery query, CancellationToken ct) =>
        (await store.ListAsync(query.PublishedOnly, ct)).Select(r => r.ToDto()).ToList();
}

internal sealed class CurrentManifestHandler(IReleaseStore store) : IQueryHandler<CurrentManifestQuery, Result<SignedManifest>>
{
    public async Task<Result<SignedManifest>> HandleAsync(CurrentManifestQuery query, CancellationToken ct) =>
        await store.CurrentAsync(query.Channel, ct) is { } current ? new SignedManifest(current.Manifest, current.Signature) : ReleasesErrors.NoRelease;
}

public static class DependencyInjection
{
    public static IServiceCollection AddReleasesApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddHandlersFrom(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
