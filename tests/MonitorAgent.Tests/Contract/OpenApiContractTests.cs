using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorAgent.Tests.Api;

namespace MonitorAgent.Tests.Contract;

/// <summary>
/// The API's OpenAPI document is checked in (openapi.v1.json). A change that would break the portal, the admin app or
/// an installed Monitor Agent (an endpoint removed, a field removed, a field newly required) fails here. Any other
/// change also fails until the file is updated, so every contract change shows up in review:
///
///   UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiContract
/// </summary>
public class OpenApiContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly string SnapshotPath = Path.Combine(FindTestsFolder(), "Contract", "openapi.v1.json");

    [Fact]
    public async Task Nothing_existing_clients_rely_on_was_removed_or_made_stricter()
    {
        var current = await CurrentAsync();
        var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(SnapshotPath))!;
        var breaks = BreakingChanges(snapshot, current).ToList();
        Assert.True(breaks.Count == 0, "Breaking API changes:\n" + string.Join('\n', breaks));
    }

    [Fact]
    public async Task The_checked_in_contract_matches_the_api()
    {
        var current = Normalize(await CurrentAsync());
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1") await File.WriteAllTextAsync(SnapshotPath, current);
        var snapshot = Normalize(JsonNode.Parse(await File.ReadAllTextAsync(SnapshotPath))!);
        Assert.True(snapshot == current,
            "The API contract changed. Check the change is intended, then run UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiContract and commit tests/MonitorAgent.Tests/Contract/openapi.v1.json.");
    }

    private async Task<JsonNode> CurrentAsync()
    {
        var response = await factory.CreateTestClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    internal static IEnumerable<string> BreakingChanges(JsonNode before, JsonNode after)
    {
        var oldPaths = before["paths"]?.AsObject() ?? [];
        var newPaths = after["paths"]?.AsObject() ?? [];
        foreach (var (path, operations) in oldPaths)
        {
            foreach (var (method, _) in operations!.AsObject())
            {
                if (newPaths[path]?[method] is null) yield return $"{method.ToUpperInvariant()} {path} was removed";
            }
        }

        var oldSchemas = before["components"]?["schemas"]?.AsObject() ?? [];
        var newSchemas = after["components"]?["schemas"]?.AsObject() ?? [];
        foreach (var (name, schema) in oldSchemas)
        {
            if (newSchemas[name] is not { } now)
            {
                yield return $"schema {name} was removed";
                continue;
            }
            foreach (var (property, _) in schema!["properties"]?.AsObject() ?? [])
            {
                if (now["properties"]?[property] is null) yield return $"{name}.{property} was removed";
            }
            var wasRequired = Names(schema["required"]);
            foreach (var required in Names(now["required"]).Except(wasRequired)) yield return $"{name}.{required} is now required";
        }
    }

    private static HashSet<string> Names(JsonNode? array) =>
        array is JsonArray items ? items.Select(i => i!.GetValue<string>()).ToHashSet() : [];

    /// <summary>Sorted keys and fixed indentation, so the file diffs cleanly whatever order the generator uses.</summary>
    private static string Normalize(JsonNode node) =>
        JsonSerializer.Serialize(Sort(node), Indented).ReplaceLineEndings("\n") + "\n";

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sort).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };

    private static string FindTestsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MonitorAgent.Tests.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Can't find the test project folder.");
    }
}

public class OpenApiBreakingChangeRulesTests
{
    private static JsonNode Doc(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void Removing_an_endpoint_a_field_or_tightening_a_request_is_breaking_adding_is_not()
    {
        var before = Doc("""
            {"paths":{"/a":{"get":{}},"/b":{"post":{}}},
             "components":{"schemas":{"Req":{"properties":{"x":{},"y":{}},"required":["x"]}}}}
            """);
        var added = Doc("""
            {"paths":{"/a":{"get":{},"put":{}},"/b":{"post":{}},"/c":{"get":{}}},
             "components":{"schemas":{"Req":{"properties":{"x":{},"y":{},"z":{}},"required":["x"]},"New":{}}}}
            """);
        Assert.Empty(OpenApiContractTests.BreakingChanges(before, added));

        var broken = Doc("""
            {"paths":{"/a":{"get":{}}},
             "components":{"schemas":{"Req":{"properties":{"x":{}},"required":["x","y"]}}}}
            """);
        Assert.Equal(["POST /b was removed", "Req.y was removed", "Req.y is now required"], OpenApiContractTests.BreakingChanges(before, broken));
    }
}
