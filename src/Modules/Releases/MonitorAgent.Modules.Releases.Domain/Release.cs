using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Releases.Domain;

public enum ReleaseStatus { Draft, Published, Withdrawn }

/// <summary>One download inside a release, for one platform such as win-x64.</summary>
public sealed record ReleasePackage(string Platform, string Url, string Sha256, long Size);

/// <summary>
/// A version of the Monitor Agent program. The manifest bytes and their signature are kept exactly as the publisher signed
/// them: the program checks the signature over these bytes, so the website never rewrites them.
/// </summary>
public sealed class Release : AggregateRoot
{
    public const string StableChannel = "stable";

    private readonly List<ReleasePackage> _packages = [];

    private Release() { }

    public string Channel { get; private set; } = StableChannel;
    public int Major { get; private set; }
    public int Minor { get; private set; }
    public int Patch { get; private set; }
    public string Version => $"{Major}.{Minor}.{Patch}";
    public string? Notes { get; private set; }
    public byte[] Manifest { get; private set; } = [];
    public string Signature { get; private set; } = "";
    public ReleaseStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public IReadOnlyList<ReleasePackage> Packages => _packages;

    public static Release Draft(string channel, Version version, string? notes, IEnumerable<ReleasePackage> packages, byte[] manifest,
        string signature, DateTimeOffset now)
    {
        var release = new Release
        {
            Channel = channel,
            Major = version.Major,
            Minor = Math.Max(0, version.Minor),
            Patch = Math.Max(0, version.Build),
            Notes = notes,
            Manifest = manifest,
            Signature = signature,
            Status = ReleaseStatus.Draft,
            CreatedAt = now,
        };
        release._packages.AddRange(packages);
        return release;
    }

    public bool IsNewerThan(Release other) => (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch)) > 0;

    /// <summary>Makes this the version programs on <see cref="Channel"/> are offered. Only a version newer than the current one.</summary>
    public Result Publish(Release? current, DateTimeOffset now)
    {
        if (Status != ReleaseStatus.Draft) return ReleasesErrors.NotDraft;
        if (current is not null && !IsNewerThan(current)) return ReleasesErrors.VersionNotNewer;
        Status = ReleaseStatus.Published;
        PublishedAt = now;
        return Result.Success();
    }

    /// <summary>Stops offering this version, for example after a bad release. Programs that already installed it keep it.</summary>
    public Result Withdraw()
    {
        if (Status != ReleaseStatus.Published) return ReleasesErrors.NotPublished;
        Status = ReleaseStatus.Withdrawn;
        return Result.Success();
    }
}

/// <summary>Error codes the Releases endpoints return. They are part of the API contract.</summary>
public static class ReleasesErrors
{
    public static readonly Error NotFound = Error.NotFound("RELEASE_NOT_FOUND", "No such release.");
    public static readonly Error NoRelease = Error.NotFound("NO_RELEASE", "No version has been published on this channel yet.");
    public static readonly Error KeyNotConfigured = Error.Unavailable("SIGNING_KEY_NOT_CONFIGURED", "Set Releases:PublicKey before adding releases.");
    public static readonly Error InvalidSignature = Error.Validation("INVALID_RELEASE_SIGNATURE", "The manifest is not signed with the publisher's key.");
    public static Error InvalidManifest(string reason) => Error.Validation("INVALID_MANIFEST", reason);
    public static readonly Error Exists = Error.Conflict("RELEASE_EXISTS", "This version was already added on this channel.");
    public static readonly Error VersionNotNewer = Error.Conflict("VERSION_NOT_NEWER", "Only a version newer than the one offered now can be published.");
    public static readonly Error NotDraft = Error.Conflict("RELEASE_NOT_DRAFT", "Only a draft can be published.");
    public static readonly Error NotPublished = Error.Conflict("RELEASE_NOT_PUBLISHED", "Only a published release can be withdrawn.");
}
