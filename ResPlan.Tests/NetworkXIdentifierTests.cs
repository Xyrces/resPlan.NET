using System.Globalization;
using Pickle.Net;
using ResPlan.Library;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests;

public class NetworkXIdentifierTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void FloatAndStringIdentifiersRemainDistinctAcrossProtocolsAndMessagePack(int protocol)
    {
        var plan = LoadGraph(protocol, 1.0, "1");

        AssertIdentifiersAndEdge(plan, "1.0", "1");
        AssertIdentifiersAndEdge(PlanSerializer.Deserialize(PlanSerializer.Serialize(plan)), "1.0", "1");
    }

    // Expected spellings were checked with CPython 3.14 str(float), independently of the native writer.
    [Theory]
    [InlineData(0.0, "0.0")]
    [InlineData(-0.0, "-0.0")]
    [InlineData(1.0, "1.0")]
    [InlineData(-1.0, "-1.0")]
    [InlineData(1.5, "1.5")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(1e16, "1e+16")]
    [InlineData(-1e16, "-1e+16")]
    [InlineData(1e-4, "0.0001")]
    [InlineData(1e-5, "1e-05")]
    [InlineData(-1e-5, "-1e-05")]
    [InlineData(1e-6, "1e-06")]
    [InlineData(1e100, "1e+100")]
    [InlineData(1.2345678901234567, "1.2345678901234567")]
    [InlineData(1000000000000000.1, "1000000000000000.1")]
    [InlineData(double.PositiveInfinity, "inf")]
    [InlineData(double.NegativeInfinity, "-inf")]
    [InlineData(double.NaN, "nan")]
    [InlineData(double.Epsilon, "5e-324")]
    [InlineData(double.MaxValue, "1.7976931348623157e+308")]
    [InlineData(double.MinValue, "-1.7976931348623157e+308")]
    [InlineData(2.2250738585072014e-308, "2.2250738585072014e-308")]
    public void FloatIdentifiersMatchPythonForNodesAndAdjacency(double value, string expected)
    {
        AssertIdentifiersAndEdge(LoadGraph(5, value, "peer"), expected, "peer");
    }

    [Fact]
    public void FloatIdentifierFormattingIsIndependentOfCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            AssertIdentifiersAndEdge(LoadGraph(5, 1234.5, "peer"), "1234.5", "peer");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void RejectsIdentifiersThatReallyCollideAfterPythonStringConversion()
    {
        var exception = Assert.Throws<InvalidDataException>(() => LoadGraph(5, 1.0, "1.0"));
        Assert.Contains("node IDs collide after conversion: '1.0'", exception.Message);
    }

    private static void AssertIdentifiersAndEdge(Plan plan, string first, string second)
    {
        var graph = Assert.IsType<Graph>(plan.SourceGraph);
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal(first, graph.Nodes[first].Id);
        Assert.Equal(second, graph.Nodes[second].Id);
        var edge = Assert.Single(graph.Edges);
        Assert.True((edge.SourceId == first && edge.TargetId == second) ||
                    (edge.SourceId == second && edge.TargetId == first),
            $"Expected an edge between '{first}' and '{second}', got '{edge.SourceId}' and '{edge.TargetId}'.");
    }

    private static Plan LoadGraph(int protocol, object first, object second)
    {
        var edge = new Dictionary<string, object?>();
        var state = new Dictionary<string, object?>
        {
            ["graph"] = new Dictionary<string, object?>(),
            ["_node"] = new Dictionary<object, object?>
            {
                [first] = new Dictionary<string, object?>(),
                [second] = new Dictionary<string, object?>()
            },
            ["_adj"] = new Dictionary<object, object?>
            {
                [first] = new Dictionary<object, object?> { [second] = edge },
                [second] = new Dictionary<object, object?> { [first] = edge }
            }
        };
        var registry = new PickleRegistry();
        registry.Register<GraphValue>("networkx.classes.graph", "Graph", (_, _) => throw new NotSupportedException(),
            value => new PickleReduction(new PickleGlobal("networkx.classes.graph", "Graph"), PickleTuple.Empty, value.State));
        var records = new[] { new Dictionary<string, object?> { ["graph"] = new GraphValue(state) } };
        var bytes = PickleSerializer.Dumps(records, protocol, new PicklerOptions { Registry = registry });
        return Assert.Single(NativePickleLoader.Load(new MemoryStream(bytes)));
    }

    private sealed record GraphValue(Dictionary<string, object?> State);
}
