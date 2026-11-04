using System.Text.Json;
using NetTopologySuite.Geometries;
using ResPlan.Library;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests;

public class ShapelyEmptyGeometryCompatibilityTests
{
    // Actual CPython/Shapely payloads exercise native loading independently of
    // the native pickle writer, including the dimensions of empty sequences.
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "EmptyGeometries", "fixtures.json");

    public static IEnumerable<object[]> PythonCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
            for (var protocol = 0; protocol <= 5; protocol++)
                yield return [fixture.GetProperty("name").GetString()!, protocol];
    }

    [Theory]
    [MemberData(nameof(PythonCases))]
    public void PreservesEmptySourceGraphGeometrySchema(string name, int protocol)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath));
        var fixture = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);
        var payload = Convert.FromBase64String(fixture.GetProperty("payloads")
            .GetProperty(protocol.ToString(System.Globalization.CultureInfo.InvariantCulture)).GetString()!);
        var expected = fixture.GetProperty("expected");
        var plan = Assert.Single(NativePickleLoader.Load(new MemoryStream(payload)));

        AssertMatchesPython(plan, expected);
        AssertMatchesPython(PlanSerializer.Deserialize(PlanSerializer.Serialize(plan)), expected);
    }

    private static void AssertMatchesPython(Plan plan, JsonElement expected)
    {
        var graph = Assert.IsType<Graph>(plan.SourceGraph);
        var geometry = Assert.Single(graph.Nodes).Value.Geometry;
        Assert.NotNull(geometry);
        Assert.Equal(expected.GetProperty("geometry_type").GetString(), geometry.GeometryType);
        Assert.Equal(expected.GetProperty("is_empty").GetBoolean(), geometry.IsEmpty);
        Assert.Equal(expected.GetProperty("srid").GetInt32(), geometry.SRID);

        var sequence = geometry switch
        {
            Point point => point.CoordinateSequence,
            Polygon polygon => polygon.ExteriorRing.CoordinateSequence,
            _ => throw new InvalidOperationException($"Unexpected fixture geometry: {geometry.GeometryType}")
        };
        Assert.Equal(0, sequence.Count);
        Assert.Equal(expected.GetProperty("dimension").GetInt32(), sequence.Dimension);
        Assert.Equal(expected.GetProperty("has_m").GetBoolean() ? 1 : 0, sequence.Measures);
        Assert.Equal(expected.GetProperty("has_z").GetBoolean(), sequence.HasZ);
        Assert.Equal(expected.GetProperty("has_m").GetBoolean(), sequence.HasM);
    }
}
