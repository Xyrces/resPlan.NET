using System.Text.Json;
using ResPlan.Library;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests;

public class GraphIdentifierCompatibilityTests
{
    // Payloads and expectations come from actual CPython/NetworkX/NumPy, rather
    // than the native writer. Generation is optional and never runs in tests.
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "GraphIdentifiers", "fixtures.json");

    public static IEnumerable<object[]> PythonCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
            for (var protocol = 0; protocol <= 5; protocol++)
                yield return [fixture.GetProperty("name").GetString()!, protocol];
    }

    [Theory]
    [MemberData(nameof(PythonCases))]
    public void PreservesPythonGraphIdentityAndScalarMetadata(string name, int protocol)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        var fixture = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);
        var payload = Convert.FromBase64String(fixture.GetProperty("payloads")
            .GetProperty(protocol.ToString(System.Globalization.CultureInfo.InvariantCulture)).GetString()!);
        if (fixture.TryGetProperty("expected_error", out var expectedError))
        {
            var exception = Assert.Throws<InvalidDataException>(() => NativePickleLoader.Load(new MemoryStream(payload)));
            Assert.Contains(expectedError.GetString()!, exception.Message);
            return;
        }
        var expected = fixture.GetProperty("expected");
        var plan = Assert.Single(NativePickleLoader.Load(new MemoryStream(payload)));

        AssertMatchesPython(plan, expected);
        AssertMatchesPython(PlanSerializer.Deserialize(PlanSerializer.Serialize(plan)), expected);
    }

    private static void AssertMatchesPython(Plan plan, JsonElement expected)
    {
        var half = expected.GetProperty("float16").GetDouble();
        var single = expected.GetProperty("float32").GetDouble();
        Assert.Equal(half, plan.WallWidth);
        Assert.Equal(half, Assert.IsType<double>(plan.Metadata["float16"]));
        Assert.Equal(single, Assert.IsType<double>(plan.Metadata["float32"]));
        var nested = Assert.IsAssignableFrom<System.Collections.IDictionary>(plan.Metadata["nested"]);
        var values = Assert.IsAssignableFrom<System.Collections.IList>(nested["values"]);
        Assert.Equal(half, Assert.IsType<double>(values[0]));
        Assert.Equal(single, Assert.IsType<double>(values[1]));
        Assert.Equal(0.1, Assert.IsType<double>(values[2]));

        var graph = Assert.IsType<Graph>(plan.SourceGraph);
        var identifiers = expected.GetProperty("nodes").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(identifiers.Order(StringComparer.Ordinal), graph.Nodes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(single, Assert.IsType<double>(graph.Metadata["measurement"]));
        foreach (var (identifier, node) in graph.Nodes)
        {
            Assert.Equal(identifier, node.Id);
            Assert.Equal(expected.GetProperty("node_type").GetString(), node.Type);
            Assert.Equal(half, node.Area);
            Assert.Equal(half, Assert.IsType<double>(node.Metadata["measurement"]));
        }

        var expectedEdges = expected.GetProperty("edges").EnumerateArray()
            .Select(edge => OrderedPair(edge[0].GetString()!, edge[1].GetString()!))
            .OrderBy(edge => edge.First, StringComparer.Ordinal).ThenBy(edge => edge.Second, StringComparer.Ordinal);
        var actualEdges = graph.Edges.Select(edge => OrderedPair(edge.SourceId, edge.TargetId))
            .OrderBy(edge => edge.First, StringComparer.Ordinal).ThenBy(edge => edge.Second, StringComparer.Ordinal);
        Assert.Equal(expectedEdges, actualEdges);
        Assert.All(graph.Edges, edge =>
        {
            Assert.Equal(expected.GetProperty("edge_type").GetString(), edge.Type);
            Assert.Equal(single, Assert.IsType<double>(edge.Metadata["measurement"]));
        });
    }

    private static (string First, string Second) OrderedPair(string first, string second)
        => string.CompareOrdinal(first, second) <= 0 ? (first, second) : (second, first);
}
