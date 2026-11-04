using System.Buffers.Binary;
using MessagePack;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using NetTopologySuite.IO;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests;

public class GeometryDimensionSerializationTests
{
    private static readonly GeometryFactory Factory = new(
        new PrecisionModel(), 4326, PackedCoordinateSequenceFactory.DoubleFactory);

    public static IEnumerable<object[]> GeometryCases()
    {
        foreach (var (dimension, measures) in new[] { (2, 0), (3, 0), (3, 1), (4, 1) })
        {
            foreach (string kind in new[] { "Point", "LineString", "Polygon" })
            {
                yield return new object[] { kind, dimension, measures, false };
                yield return new object[] { kind, dimension, measures, true };
            }

            foreach (string kind in new[] { "MultiPoint", "MultiLineString", "MultiPolygon", "GeometryCollection" })
                yield return new object[] { kind, dimension, measures, false };
        }
    }

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public void RoundTripPreservesCoordinateSchemaAndEwkb(string kind, int dimension, int measures, bool empty)
    {
        Geometry source = CreateGeometry(kind, dimension, measures, empty);

        byte[] payload = MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options);
        Geometry result = MessagePackSerializer.Deserialize<Geometry>(payload, ResPlanMessagePackResolver.Options);

        AssertGeometryEqual(source, result);
        // An explicit EWKB schema gives an independent expected binary representation.
        byte[] expectedWkb = new WKBWriter(ByteOrder.LittleEndian, handleSRID: true,
            emitZ: dimension - measures > 2, emitM: measures > 0).Write(source);
        Assert.Equal(expectedWkb, MessagePackSerializer.Deserialize<byte[]>(payload));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    public void MissingOrdinateValuesDoNotChangeTheDeclaredSchema(int dimension, int measures)
    {
        CoordinateSequence sequence = CreateSequence(dimension, measures, (1, 2), (3, 4));
        for (int point = 0; point < sequence.Count; point++)
            for (int ordinate = 2; ordinate < sequence.Dimension; ordinate++)
                sequence.SetOrdinate(point, ordinate, double.NaN);

        LineString source = Factory.CreateLineString(sequence);
        LineString result = MessagePackSerializer.Deserialize<LineString>(
            MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options),
            ResPlanMessagePackResolver.Options);

        AssertGeometryEqual(source, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedDimensionComponentsRetainTheirIndividualSchemas(bool multiPoint)
    {
        Point[] points = new[] { (2, 0), (3, 0), (3, 1), (4, 1) }
            .Select(schema => Factory.CreatePoint(CreateSequence(schema.Item1, schema.Item2, (1, 2))))
            .ToArray();
        Geometry source = multiPoint
            ? Factory.CreateMultiPoint(points)
            : Factory.CreateGeometryCollection(new Geometry[]
            {
                points[0],
                Factory.CreateGeometryCollection(points[1..]),
                CreateGeometry("Polygon", 3, 1, empty: false)
            });

        Geometry result = MessagePackSerializer.Deserialize<Geometry>(
            MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options),
            ResPlanMessagePackResolver.Options);

        AssertGeometryEqual(source, result);
    }

    [Fact]
    public void MultiPointWithMostlyXyChildrenDoesNotApplyXyzmSizeToEveryPoint()
    {
        // NTS uses the collection header's schema for a minimum-payload-size check.
        // A union XYZM header would incorrectly reject these four smaller XY points.
        Point[] points = Enumerable.Range(0, 4)
            .Select(i => Factory.CreatePoint(CreateSequence(2, 0, (i, i + 1))))
            .Append(Factory.CreatePoint(CreateSequence(4, 1, (10, 20))))
            .ToArray();
        MultiPoint source = Factory.CreateMultiPoint(points);

        MultiPoint result = MessagePackSerializer.Deserialize<MultiPoint>(
            MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options),
            ResPlanMessagePackResolver.Options);

        AssertGeometryEqual(source, result);
    }

    [Fact]
    public void NestedEmptyComponentsKeepTheirSchemasAndIndividualSrids()
    {
        Geometry emptyPolygon = CreateGeometry("Polygon", 3, 1, empty: true, srid: 3857);
        GeometryCollection source = Factory.CreateGeometryCollection(new Geometry[]
        {
            CreateGeometry("Point", 2, 0, empty: true),
            Factory.CreateGeometryCollection(new Geometry[]
            {
                CreateGeometry("Point", 3, 0, empty: true),
                emptyPolygon,
                CreateGeometry("Polygon", 4, 1, empty: true),
                CreateGeometry("LineString", 3, 1, empty: false)
            })
        });
        Assert.Equal(3857, source.GetGeometryN(1).GetGeometryN(1).SRID);

        GeometryCollection result = MessagePackSerializer.Deserialize<GeometryCollection>(
            MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options),
            ResPlanMessagePackResolver.Options);

        AssertGeometryEqual(source, result);
    }

    public static IEnumerable<object[]> ExplicitChildSridCases()
    {
        foreach (int parentSrid in new[] { 4326, 0 })
            foreach (int childSrid in new[] { -1, -2, 0, 3857 })
                foreach (var (kind, empty) in new[]
                {
                    ("Point", false), ("LineString", false), ("Polygon", false),
                    ("Point", true), ("Polygon", true)
                })
                    yield return new object[] { parentSrid, childSrid, kind, empty };
    }

    [Theory]
    [MemberData(nameof(ExplicitChildSridCases))]
    public void ExplicitChildSridOverridesTheParentEvenWhenNegative(
        int parentSrid, int childSrid, string kind, bool empty)
    {
        var parentFactory = new GeometryFactory(new PrecisionModel(), parentSrid);
        Geometry child = CreateGeometry(kind, 3, 1, empty, childSrid);
        GeometryCollection source = parentFactory.CreateGeometryCollection(new[] { child });

        GeometryCollection roundTrip = MessagePackSerializer.Deserialize<GeometryCollection>(
            MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options),
            ResPlanMessagePackResolver.Options);
        AssertGeometryEqual(source, roundTrip);

        foreach (ByteOrder byteOrder in new[] { ByteOrder.LittleEndian, ByteOrder.BigEndian })
        {
            // Build the collection envelope independently; the child always has
            // an explicit SRID field, including -1, -2 and zero.
            byte[] wkb = CollectionWkb(byteOrder, parentSrid,
                XymWkb(child, byteOrder, includeSrid: true));
            GeometryCollection decoded = MessagePackSerializer.Deserialize<GeometryCollection>(
                MessagePackSerializer.Serialize(wkb), ResPlanMessagePackResolver.Options);
            AssertGeometryEqual(source, decoded);
        }
    }

    [Theory]
    [InlineData(4326, -1)]
    [InlineData(4326, -2)]
    [InlineData(0, -1)]
    [InlineData(0, -2)]
    public void NestedNegativeSridIsInheritedUntilAnotherExplicitOverride(int parentSrid, int nestedSrid)
    {
        var parentFactory = new GeometryFactory(new PrecisionModel(), parentSrid);
        var nestedFactory = new GeometryFactory(new PrecisionModel(), nestedSrid);
        Geometry inheritedPoint = CreateGeometry("Point", 3, 1, empty: false, nestedSrid);
        Geometry inheritedEmptyPolygon = CreateGeometry("Polygon", 3, 1, empty: true, nestedSrid);
        Geometry zeroOverride = CreateGeometry("LineString", 3, 1, empty: false, srid: 0);
        Geometry inheritedEmptyPoint = CreateGeometry("Point", 3, 1, empty: true, nestedSrid);
        Geometry inheritedPolygon = CreateGeometry("Polygon", 3, 1, empty: false, nestedSrid);
        GeometryCollection inner = nestedFactory.CreateGeometryCollection(
            new[] { inheritedEmptyPoint, inheritedPolygon });
        GeometryCollection nested = nestedFactory.CreateGeometryCollection(
            new[] { inheritedPoint, inheritedEmptyPolygon, zeroOverride, inner });
        GeometryCollection source = parentFactory.CreateGeometryCollection(new Geometry[] { nested });

        GeometryCollection roundTrip = MessagePackSerializer.Deserialize<GeometryCollection>(
            MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options),
            ResPlanMessagePackResolver.Options);
        AssertGeometryEqual(source, roundTrip);

        foreach (ByteOrder byteOrder in new[] { ByteOrder.LittleEndian, ByteOrder.BigEndian })
        {
            // Only the nested collection declares the negative SRID. Its children,
            // and the next collection level, must inherit that signed value.
            byte[] wkb = CollectionWkb(byteOrder, parentSrid,
                CollectionWkb(byteOrder, nestedSrid,
                    XymWkb(inheritedPoint, byteOrder, includeSrid: false),
                    XymWkb(inheritedEmptyPolygon, byteOrder, includeSrid: false),
                    XymWkb(zeroOverride, byteOrder, includeSrid: true),
                    CollectionWkb(byteOrder, null,
                        XymWkb(inheritedEmptyPoint, byteOrder, includeSrid: false),
                        XymWkb(inheritedPolygon, byteOrder, includeSrid: false))));
            GeometryCollection decoded = MessagePackSerializer.Deserialize<GeometryCollection>(
                MessagePackSerializer.Serialize(wkb), ResPlanMessagePackResolver.Options);
            AssertGeometryEqual(source, decoded);
        }
    }

    [Theory]
    [InlineData("MultiPoint")]
    [InlineData("MultiLineString")]
    [InlineData("MultiPolygon")]
    [InlineData("GeometryCollection")]
    public void EmptyCollectionsKeepTheirTypeAndSrid(string kind)
    {
        Geometry source = kind switch
        {
            "MultiPoint" => Factory.CreateMultiPoint(),
            "MultiLineString" => Factory.CreateMultiLineString(),
            "MultiPolygon" => Factory.CreateMultiPolygon(),
            _ => Factory.CreateGeometryCollection()
        };

        Geometry result = MessagePackSerializer.Deserialize<Geometry>(
            MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options),
            ResPlanMessagePackResolver.Options);

        AssertGeometryEqual(source, result);
        Assert.Equal(0, result.NumGeometries);
    }

    public static IEnumerable<object[]> LegacyEmptyGeometryCases()
    {
        foreach (string kind in new[] { "Point", "Polygon" })
            foreach (var (dimension, measures) in new[] { (2, 0), (3, 0), (3, 1), (4, 1) })
                foreach (ByteOrder byteOrder in new[] { ByteOrder.LittleEndian, ByteOrder.BigEndian })
                    foreach (bool iso in new[] { false, true })
                        yield return new object[] { kind, dimension, measures, byteOrder, iso };
    }

    [Theory]
    [MemberData(nameof(LegacyEmptyGeometryCases))]
    public void ReadsEmptyGeometrySchemaFromBothByteOrdersAndIsoOrEwkb(
        string kind, int dimension, int measures, ByteOrder byteOrder, bool iso)
    {
        // ISO WKB has no SRID field; the reader's documented default is -1.
        Geometry source = CreateGeometry(kind, dimension, measures, empty: true, srid: iso ? -1 : 4326);
        var writer = new WKBWriter(byteOrder, handleSRID: !iso,
            emitZ: dimension - measures > 2, emitM: measures > 0) { Strict = iso };
        byte[] legacyPayload = MessagePackSerializer.Serialize(writer.Write(source));

        Geometry result = MessagePackSerializer.Deserialize<Geometry>(
            legacyPayload, ResPlanMessagePackResolver.Options);

        AssertGeometryEqual(source, result);
    }

    [Fact]
    public void RejectsPolygonRingsWithDifferentSchemasInsteadOfInventingOrDiscardingOrdinates()
    {
        LinearRing shell = Factory.CreateLinearRing(CreateSequence(2, 0,
            (0, 0), (10, 0), (10, 10), (0, 10), (0, 0)));
        LinearRing hole = Factory.CreateLinearRing(CreateSequence(3, 1,
            (2, 2), (2, 4), (4, 4), (4, 2), (2, 2)));
        Polygon source = Factory.CreatePolygon(shell, new[] { hole });

        var error = Assert.Throws<MessagePackSerializationException>(() =>
            MessagePackSerializer.Serialize(source, ResPlanMessagePackResolver.Options));

        Assert.Contains("every ring of a polygon", error.ToString(), StringComparison.Ordinal);
    }

    private static byte[] XymWkb(Geometry geometry, ByteOrder byteOrder, bool includeSrid)
        => new WKBWriter(byteOrder, handleSRID: true, emitZ: false, emitM: true)
        { HandleSRID = includeSrid }.Write(geometry);

    private static byte[] CollectionWkb(ByteOrder byteOrder, int? srid, params byte[][] children)
    {
        // EWKB GeometryCollection header: byte order, type, optional signed SRID,
        // child count, then independently encoded child geometries.
        byte[] bytes = new byte[(srid.HasValue ? 13 : 9) + children.Sum(child => child.Length)];
        bytes[0] = (byte)byteOrder;
        int offset = 1;
        WriteUInt32(7u | (srid.HasValue ? 0x20000000u : 0u));
        if (srid.HasValue) WriteUInt32(unchecked((uint)srid.Value));
        WriteUInt32((uint)children.Length);
        foreach (byte[] child in children)
        {
            child.CopyTo(bytes, offset);
            offset += child.Length;
        }
        return bytes;

        void WriteUInt32(uint value)
        {
            if (byteOrder == ByteOrder.LittleEndian)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
            else
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
            offset += sizeof(uint);
        }
    }

    private static Geometry CreateGeometry(string kind, int dimension, int measures, bool empty, int srid = 4326)
    {
        var factory = srid == Factory.SRID
            ? Factory
            : new GeometryFactory(new PrecisionModel(), srid, PackedCoordinateSequenceFactory.DoubleFactory);
        Point point = factory.CreatePoint(empty
            ? CreateSequence(dimension, measures)
            : CreateSequence(dimension, measures, (1, 2)));
        LineString line = factory.CreateLineString(empty
            ? CreateSequence(dimension, measures)
            : CreateSequence(dimension, measures, (1, 2), (3, 4)));
        LinearRing shell = factory.CreateLinearRing(empty
            ? CreateSequence(dimension, measures)
            : CreateSequence(dimension, measures, (0, 0), (10, 0), (10, 10), (0, 10), (0, 0)));
        Polygon polygon = empty
            ? factory.CreatePolygon(shell)
            : factory.CreatePolygon(shell, new[]
            {
                factory.CreateLinearRing(CreateSequence(dimension, measures,
                    (2, 2), (2, 4), (4, 4), (4, 2), (2, 2)))
            });

        return kind switch
        {
            "Point" => point,
            "LineString" => line,
            "Polygon" => polygon,
            "MultiPoint" => factory.CreateMultiPoint(new[] { point, (Point)point.Copy() }),
            "MultiLineString" => factory.CreateMultiLineString(new[] { line, (LineString)line.Copy() }),
            "MultiPolygon" => factory.CreateMultiPolygon(new[] { polygon }),
            "GeometryCollection" => factory.CreateGeometryCollection(new Geometry[] { point, line, polygon }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static CoordinateSequence CreateSequence(int dimension, int measures, params (double X, double Y)[] points)
    {
        CoordinateSequence sequence = Factory.CoordinateSequenceFactory.Create(points.Length, dimension, measures);
        for (int i = 0; i < points.Length; i++)
        {
            sequence.SetOrdinate(i, 0, points[i].X);
            sequence.SetOrdinate(i, 1, points[i].Y);
            if (sequence.HasZ) sequence.SetOrdinate(i, Ordinate.Z, 10 + points[i].X);
            if (sequence.HasM) sequence.SetOrdinate(i, Ordinate.M, 100 + points[i].Y);
        }
        return sequence;
    }

    private static void AssertGeometryEqual(Geometry expected, Geometry actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.SRID, actual.SRID);
        Assert.Equal(expected.IsEmpty, actual.IsEmpty);
        switch (expected)
        {
            case Point point:
                AssertSequenceEqual(point.CoordinateSequence, ((Point)actual).CoordinateSequence);
                break;
            case LineString line:
                AssertSequenceEqual(line.CoordinateSequence, ((LineString)actual).CoordinateSequence);
                break;
            case Polygon polygon:
                var actualPolygon = (Polygon)actual;
                AssertGeometryEqual(polygon.ExteriorRing, actualPolygon.ExteriorRing);
                Assert.Equal(polygon.NumInteriorRings, actualPolygon.NumInteriorRings);
                for (int i = 0; i < polygon.NumInteriorRings; i++)
                    AssertGeometryEqual(polygon.GetInteriorRingN(i), actualPolygon.GetInteriorRingN(i));
                break;
            case GeometryCollection collection:
                Assert.Equal(collection.NumGeometries, actual.NumGeometries);
                for (int i = 0; i < collection.NumGeometries; i++)
                    AssertGeometryEqual(collection.GetGeometryN(i), actual.GetGeometryN(i));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expected));
        }
    }

    private static void AssertSequenceEqual(CoordinateSequence expected, CoordinateSequence actual)
    {
        Assert.Equal(expected.Dimension, actual.Dimension);
        Assert.Equal(expected.Measures, actual.Measures);
        Assert.Equal(expected.HasZ, actual.HasZ);
        Assert.Equal(expected.HasM, actual.HasM);
        Assert.Equal(expected.Count, actual.Count);
        for (int point = 0; point < expected.Count; point++)
            for (int ordinate = 0; ordinate < expected.Dimension; ordinate++)
                Assert.Equal(expected.GetOrdinate(point, ordinate), actual.GetOrdinate(point, ordinate));
    }
}
