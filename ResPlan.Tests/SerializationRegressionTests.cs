using MessagePack;
using NetTopologySuite.Geometries;
using ResPlan.Library;
using ResPlan.Library.Data;
using Xunit;
using System.Buffers;
using System.Collections;

namespace ResPlan.Tests;

public class SerializationRegressionTests
{
    private static readonly MessagePackSerializerOptions Options = ResPlanMessagePackResolver.Options;

    [Fact]
    public void PlanRoundTripPreservesSridElevationAndMeasure()
    {
        var factory = new GeometryFactory(new PrecisionModel(), srid: 4326);
        var point = factory.CreatePoint(new CoordinateZM(1, 2, 3, 4));
        var line = factory.CreateLineString(new Coordinate[]
        {
            new CoordinateZM(1, 2, 3, 4), new CoordinateZM(5, 6, 7, 8)
        });
        var plan = new Plan
        {
            Id = 42,
            Geometries = new() { ["walls"] = new() { line } },
            ReferenceGraph = new Graph
            {
                Nodes = new() { ["room"] = new Node { Id = "room", Geometry = point } }
            }
        };

        var result = PlanSerializer.Deserialize(PlanSerializer.Serialize(plan));

        AssertGeometryEqual(line, result.Geometries["walls"][0]);
        Assert.NotNull(result.ReferenceGraph);
        AssertGeometryEqual(point, result.ReferenceGraph.Nodes["room"].Geometry!);
    }

    [Fact]
    public void StronglyTypedGeometryFormattersRoundTripAllStandardTypes()
    {
        var factory = new GeometryFactory(new PrecisionModel(), srid: 3857);
        var point = factory.CreatePoint(new CoordinateZ(1, 2, 3));
        var line = factory.CreateLineString(new Coordinate[] { new CoordinateZ(1, 2, 3), new CoordinateZ(4, 5, 6) });
        var ring = factory.CreateLinearRing(new Coordinate[]
        {
            new CoordinateZ(0, 0, 1), new CoordinateZ(4, 0, 2),
            new CoordinateZ(4, 4, 3), new CoordinateZ(0, 0, 1)
        });
        var polygon = factory.CreatePolygon(ring);

        AssertRoundTrip(point);
        AssertRoundTrip(line);
        AssertRoundTrip(ring);
        AssertRoundTrip(polygon);
        AssertRoundTrip(factory.CreateMultiPoint(new[] { point }));
        AssertRoundTrip(factory.CreateMultiLineString(new[] { line }));
        AssertRoundTrip(factory.CreateMultiPolygon(new[] { polygon }));
        AssertRoundTrip(factory.CreateGeometryCollection(new Geometry[] { point, line, polygon }));
        AssertRoundTrip(factory.CreatePoint());
        AssertRoundTrip(factory.CreatePolygon());

        var typedList = MessagePackSerializer.Deserialize<List<Polygon>>(
            MessagePackSerializer.Serialize(new List<Polygon> { polygon }, Options), Options);
        AssertGeometryEqual(polygon, Assert.Single(typedList));
    }

    [Fact]
    public void DerivedGeometryFormatterPreservesNullAndRejectsWrongGeometryType()
    {
        Assert.Null(MessagePackSerializer.Deserialize<Point?>(
            MessagePackSerializer.Serialize<Point?>(null, Options), Options));

        byte[] point = MessagePackSerializer.Serialize(new Point(1, 2), Options);
        Assert.Throws<MessagePackSerializationException>(() =>
            MessagePackSerializer.Deserialize<Polygon>(point, Options));
    }

    [Fact]
    public void ReadsLegacyTwoDimensionalWkbBinaryPayload()
    {
        // POINT (1 2), encoded by the former default WKBWriter without SRID or Z.
        byte[] legacyWkb = Convert.FromHexString("0101000000000000000000F03F0000000000000040");
        byte[] legacyPayload = MessagePackSerializer.Serialize(legacyWkb);

        var geometry = MessagePackSerializer.Deserialize<Geometry>(legacyPayload, Options);

        var point = Assert.IsType<Point>(geometry);
        Assert.Equal(1, point.X);
        Assert.Equal(2, point.Y);
        Assert.True(double.IsNaN(point.Z));
        Assert.Equal(-1, point.SRID); // NTS's default for WKB with no embedded SRID.
    }

    [Fact]
    public void ReadsLegacyFourFieldPlanWithDefaultsForNewFields()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(4);
        writer.Write(17);
        writer.WriteMapHeader(1);
        writer.Write("walls");
        writer.WriteArrayHeader(1);
        writer.Write(Convert.FromHexString("0101000000000000000000F03F0000000000000040"));
        writer.WriteArrayHeader(4);
        foreach (double coordinate in new[] { 0d, 10d, 0d, 20d }) writer.Write(coordinate);
        writer.WriteNil();
        writer.Flush();

        var plan = PlanSerializer.Deserialize(buffer.WrittenSpan.ToArray());

        Assert.Equal(17, plan.Id);
        Assert.Equal(0.1, plan.WallWidth);
        Assert.Empty(plan.Metadata);
        Assert.Null(plan.SourceGraph);
        Assert.Null(plan.ReferenceGraph);
        Assert.Equal(new Envelope(0, 10, 0, 20), plan.Bounds);
        Assert.IsType<Point>(Assert.Single(plan.Geometries["walls"]));
    }

    [Fact]
    public void RoundTripsNormalizedMetadataAndSourceGraph()
    {
        var point = new GeometryFactory(new PrecisionModel(), srid: 4326).CreatePoint(new CoordinateZ(1, 2, 3));
        var plan = new Plan
        {
            WallWidth = 0.25,
            Metadata = new()
            {
                ["label"] = "floor one", ["enabled"] = true, ["missing"] = null,
                ["nested"] = new Dictionary<string, object?>
                {
                    ["values"] = new List<object?> { 42L, 2.5d, "text", null }
                }
            },
            SourceGraph = new Graph
            {
                Metadata = new() { ["directed"] = false },
                Nodes = new()
                {
                    ["room"] = new Node
                    {
                        Id = "room", Geometry = point, Metadata = new() { ["name"] = "Kitchen" }
                    }
                },
                Edges = new()
                {
                    new Edge { SourceId = "room", TargetId = "other", Metadata = new() { ["weight"] = 3.5d } }
                }
            }
        };

        var result = PlanSerializer.Deserialize(PlanSerializer.Serialize(plan));

        Assert.Equal(0.25, result.WallWidth);
        Assert.Equal("floor one", result.Metadata["label"]);
        Assert.Equal(true, result.Metadata["enabled"]);
        Assert.Null(result.Metadata["missing"]);
        var nested = Assert.IsAssignableFrom<IDictionary>(result.Metadata["nested"]);
        var values = Assert.IsAssignableFrom<IList>(nested["values"]);
        Assert.Equal(42, Convert.ToInt32(values[0]));
        Assert.Equal(2.5d, values[1]);
        Assert.Equal("text", values[2]);
        Assert.Null(values[3]);
        Assert.NotNull(result.SourceGraph);
        Assert.Null(result.ReferenceGraph);
        Assert.Equal(false, result.SourceGraph.Metadata["directed"]);
        Assert.Equal("Kitchen", result.SourceGraph.Nodes["room"].Metadata["name"]);
        AssertGeometryEqual(point, result.SourceGraph.Nodes["room"].Geometry!);
        Assert.Equal(3.5d, Assert.Single(result.SourceGraph.Edges).Metadata["weight"]);
    }

    [Fact]
    public void ReadsLegacyEnvelopeArrayAndRejectsInvalidLength()
    {
        byte[] legacyPayload = MessagePackSerializer.Serialize(new[] { 1d, 2d, 3d, 4d });
        var envelope = MessagePackSerializer.Deserialize<Envelope>(legacyPayload, Options);
        Assert.Equal(new Envelope(1, 2, 3, 4), envelope);

        Assert.Throws<MessagePackSerializationException>(() =>
            MessagePackSerializer.Deserialize<Envelope>(MessagePackSerializer.Serialize(new[] { 1d, 2d }), Options));
    }

    [Fact]
    public void EnvelopeFormatterEnforcesConfiguredDepthLimit()
    {
        byte[] bytes = MessagePackSerializer.Serialize(new List<Envelope> { new(1, 2, 3, 4) }, Options);
        var limited = Options.WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(1));

        Assert.Throws<MessagePackSerializationException>(() =>
            MessagePackSerializer.Deserialize<List<Envelope>>(bytes, limited));
    }

    [Fact]
    public void PlanSerializerRejectsNullInputsAndNilPayload()
    {
        Assert.Throws<ArgumentNullException>(() => PlanSerializer.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => PlanSerializer.Deserialize(null!));
        Assert.Throws<MessagePackSerializationException>(() => PlanSerializer.Deserialize(new byte[] { 0xc0 }));
    }

    [Fact]
    public void PlanSerializerRestoresLegacyNilBoundsAsAnEmptyEnvelope()
    {
        // The existing envelope format uses nil for an empty envelope.
        var result = PlanSerializer.Deserialize(PlanSerializer.Serialize(new Plan()));
        Assert.NotNull(result.Bounds);
        Assert.True(result.Bounds.IsNull);

        // Four-field legacy plan: [id, geometries, nil bounds, nil graph].
        var legacy = PlanSerializer.Deserialize(new byte[] { 0x94, 0x01, 0x80, 0xc0, 0xc0 });
        Assert.NotNull(legacy.Bounds);
        Assert.True(legacy.Bounds.IsNull);
    }

    [Fact]
    public void ConcurrentGeometryRoundTripsKeepIndependentCodecState()
    {
        Parallel.For(0, 100, i =>
        {
            var geometry = new GeometryFactory(new PrecisionModel(), srid: 4000 + i).CreateLineString(new Coordinate[]
            {
                new CoordinateZM(i, i + 1, i + 2, i + 3),
                new CoordinateZM(i + 4, i + 5, i + 6, i + 7)
            });
            AssertRoundTrip(geometry);
        });
    }

    private static void AssertRoundTrip<TGeometry>(TGeometry geometry) where TGeometry : Geometry
    {
        byte[] bytes = MessagePackSerializer.Serialize(geometry, Options);
        var result = MessagePackSerializer.Deserialize<TGeometry>(bytes, Options);
        Assert.IsType<TGeometry>(result);
        AssertGeometryEqual(geometry, result);
    }

    private static void AssertGeometryEqual(Geometry expected, Geometry actual)
    {
        Assert.Equal(expected.GeometryType, actual.GeometryType);
        Assert.Equal(expected.SRID, actual.SRID);
        Assert.True(expected.EqualsExact(actual));
        Assert.Equal(expected.Coordinates.Length, actual.Coordinates.Length);
        foreach (var (left, right) in expected.Coordinates.Zip(actual.Coordinates))
        {
            Assert.Equal(left.Z, right.Z);
            Assert.Equal(left.M, right.M);
        }
    }
}
