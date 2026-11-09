using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MonitorAgent.Tests.Contract;

/// <summary>
/// Consumer side of the Website → LicensingPlatform contract. <c>licensing-platform.openapi.v1.json</c> is a copy of
/// LicensingPlatform's own checked-in contract (<c>tests/Licensing.Tests/Contracts/openapi.v1.json</c> in that repo).
/// Every route the Website's client calls must exist in it, so a renamed or removed LicensingPlatform route fails
/// here instead of in production. When LicensingPlatform changes its contract, copy the new file over this one.
/// </summary>
public partial class LicensingPlatformConsumerContractTests
{
    private static readonly string TestsFolder = FindFolder("MonitorAgent.Tests.csproj");
    private static readonly string RepoRoot = Directory.GetParent(TestsFolder)!.Parent!.FullName;

    private static string ClientSource => File.ReadAllText(Path.Combine(RepoRoot, "src", "Modules", "Licenses",
        "MonitorAgent.Modules.Licenses.Infrastructure", "Platform", "LicensingPlatformClient.cs"));

    /// <summary>Every <c>api/v1/…</c> route literal in the client, with its parameters and query string normalised.</summary>
    private static IReadOnlyList<string> CalledRoutes() => RouteLiteral().Matches(ClientSource)
        .Select(m => Normalize("/" + m.Groups["route"].Value.Split('?')[0]))
        .Distinct().Order(StringComparer.Ordinal).ToList();

    private static HashSet<string> ProviderRoutes()
    {
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(TestsFolder, "Contract", "licensing-platform.openapi.v1.json")))!;
        return document["paths"]!.AsObject().Select(p => Normalize(p.Key)).ToHashSet(StringComparer.Ordinal);
    }

    private static string Normalize(string route) => Parameter().Replace(route, "{}");

    [Fact]
    public void The_client_calls_routes_at_all() =>
        Assert.True(CalledRoutes().Count >= 10, "Route literals were not found in LicensingPlatformClient.cs; update this test.");

    [Fact]
    public void Every_route_the_client_calls_exists_in_the_LicensingPlatform_contract()
    {
        var provider = ProviderRoutes();
        var missing = CalledRoutes().Where(r => !provider.Contains(r)).ToList();
        Assert.True(missing.Count == 0, "Routes the Website calls that LicensingPlatform does not offer:\n" + string.Join('\n', missing));
    }

    [Fact]
    public void The_copy_matches_LicensingPlatform_when_its_repository_is_checked_out_beside_this_one()
    {
        // Local runs and CI jobs that check out both repositories compare the copy with the source of truth.
        var upstream = Path.Combine(Directory.GetParent(RepoRoot)!.FullName, "LicensingPlatform", "tests", "Licensing.Tests", "Contracts", "openapi.v1.json");
        if (!File.Exists(upstream)) return;
        Assert.True(File.ReadAllText(upstream) == File.ReadAllText(Path.Combine(TestsFolder, "Contract", "licensing-platform.openapi.v1.json")),
            "LicensingPlatform's contract changed: copy its openapi.v1.json to tests/MonitorAgent.Tests/Contract/licensing-platform.openapi.v1.json and check the Website still works with it.");
    }

    [GeneratedRegex("\"(?<route>api/v1/[^\"]+)\"")]
    private static partial Regex RouteLiteral();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Parameter();

    private static string FindFolder(string marker)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, marker))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Can't find the test project folder.");
    }
}
