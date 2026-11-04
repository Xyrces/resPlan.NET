using System.Text.Json;
using ResPlan.Library;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests;

public class GraphBufferCompatibilityTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "GraphBufferBoundaries", "fixtures.json");

    public static IEnumerable<object[]> PythonCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [fixture.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(PythonCases))]
    public void PreservesPythonGraphEdgesNearRoundedBufferBoundaries(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        var fixture = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);
        var payload = Convert.FromBase64String(fixture.GetProperty("payload").GetString()!);
        var expected = fixture.GetProperty("expected");
        var plan = Assert.Single(NativePickleLoader.Load(new MemoryStream(payload)));

        AssertMatchesPython(plan, expected);
        AssertMatchesPython(PlanSerializer.Deserialize(PlanSerializer.Serialize(plan)), expected);
    }

    private static void AssertMatchesPython(Plan plan, JsonElement expected)
    {
        var graph = Assert.IsType<Graph>(plan.ReferenceGraph);
        Assert.Equal(expected.GetProperty("nodes").EnumerateArray().Select(node => node.GetString()!),
            graph.Nodes.Keys.Order(StringComparer.Ordinal));
        var expectedEdges = expected.GetProperty("edges").EnumerateArray()
            .Select(edge => (edge[0].GetString()!, edge[1].GetString()!, edge[2].GetString()!));
        var actualEdges = graph.Edges.Select(edge => string.CompareOrdinal(edge.SourceId, edge.TargetId) <= 0
                ? (edge.SourceId, edge.TargetId, edge.Type)
                : (edge.TargetId, edge.SourceId, edge.Type))
            .OrderBy(edge => edge.Item1, StringComparer.Ordinal)
            .ThenBy(edge => edge.Item2, StringComparer.Ordinal)
            .ThenBy(edge => edge.Item3, StringComparer.Ordinal);
        Assert.Equal(expectedEdges, actualEdges);
    }
}
