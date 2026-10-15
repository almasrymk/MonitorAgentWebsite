using System.Security.Cryptography;
using System.Text.Json;
using MonitorAgent.Modules.Releases.Domain;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Releases.Application;

public sealed class ReleasesOptions
{
    /// <summary>
    /// The publisher's public key (base64 SubjectPublicKeyInfo, ECDSA P-256), the same one built into the program. The private
    /// key never comes to the website: releases are signed offline with UBGMonitor's tools/UpdateSigner.
    /// </summary>
    public string PublicKey { get; set; } = "";
}

internal sealed record ParsedManifest(Version Version, string? Notes, IReadOnlyList<ReleasePackage> Packages);

/// <summary>
/// Checks a manifest the way the Monitor Agent program does (UBGMonitor UpdateSignature and UpdateManifest), so the website
/// never offers something the program would refuse.
/// </summary>
internal static class ManifestReader
{
    public const string ProductName = "MonitorAgent";
    public const int MaxBytes = 256 * 1024;

    public static bool SignatureIsValid(byte[] manifest, string signature, string publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey.Trim()), out _);
            return key.VerifyData(manifest, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static Result<ParsedManifest> Parse(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "product") != ProductName)
                return ReleasesErrors.InvalidManifest($"The manifest's product must be {ProductName}.");
            if (!System.Version.TryParse(Text(root, "version")?.Trim(), out var version))
                return ReleasesErrors.InvalidManifest("The manifest has no valid version.");

            var packages = new List<ReleasePackage>();
            if (root.TryGetProperty("packages", out var list) && list.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in list.EnumerateObject())
                {
                    var item = entry.Value;
                    if (item.ValueKind != JsonValueKind.Object
                        || !Uri.TryCreate(Text(item, "url"), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
                        || Text(item, "sha256") is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit)
                        || !item.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes) || bytes <= 0)
                        return ReleasesErrors.InvalidManifest($"The package for {entry.Name} needs an https url, a sha256 and a size.");
                    packages.Add(new ReleasePackage(entry.Name.ToLowerInvariant(), url.ToString(), hash.ToLowerInvariant(), bytes));
                }
            }
            if (packages.Count == 0) return ReleasesErrors.InvalidManifest("The manifest has no packages.");
            if (packages.Select(p => p.Platform).Distinct().Count() != packages.Count)
                return ReleasesErrors.InvalidManifest("A platform appears twice in the manifest.");

            return new ParsedManifest(version, Text(root, "notes"), packages);
        }
        catch (JsonException)
        {
            return ReleasesErrors.InvalidManifest("The manifest is not valid JSON.");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
