using System.Buffers.Binary;
using MessagePack;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace ResPlan.Library.Data;

/// <summary>Retains per-geometry XY/Z/M schemas in the existing EWKB payload.</summary>
internal static class GeometryWkb
{
    internal static byte[] Write(Geometry geometry)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteGeometry(writer, geometry, null);
        return stream.ToArray();
    }

    private static void WriteGeometry(BinaryWriter writer, Geometry geometry, int? parentSrid)
    {
        var ordinates = GetOrdinates(geometry);
        var includeSrid = parentSrid != geometry.SRID;
        if (geometry is not GeometryCollection collection)
        {
            // A writer's flags apply to every coordinate it writes. Use a fresh
            // writer per atomic geometry, rather than promoting collection children.
            var wkb = new WKBWriter(ByteOrder.LittleEndian, handleSRID: true,
                emitZ: (ordinates & Ordinates.Z) != 0, emitM: (ordinates & Ordinates.M) != 0)
            { HandleSRID = includeSrid };
            writer.Write(wkb.Write(geometry));
            return;
        }

        uint type = (uint)collection.OgcGeometryType;
        if ((ordinates & Ordinates.Z) != 0) type = (type + 1000) | 0x80000000;
        if ((ordinates & Ordinates.M) != 0) type = (type + 2000) | 0x40000000;
        if (includeSrid) type |= 0x20000000;
        writer.Write((byte)ByteOrder.LittleEndian);
        writer.Write(type);
        if (includeSrid) writer.Write(collection.SRID);
        writer.Write(collection.NumGeometries);
        for (var index = 0; index < collection.NumGeometries; index++)
            WriteGeometry(writer, collection.GetGeometryN(index), collection.SRID);
    }

    private static Ordinates GetOrdinates(Geometry geometry)
    {
        switch (geometry)
        {
            case Point point:
                return point.CoordinateSequence.Ordinates;
            case LineString line:
                return line.CoordinateSequence.Ordinates;
            case Polygon polygon:
                var ordinates = polygon.ExteriorRing.CoordinateSequence.Ordinates;
                for (var index = 0; index < polygon.NumInteriorRings; index++)
                    if (polygon.GetInteriorRingN(index).CoordinateSequence.Ordinates != ordinates)
                        throw new MessagePackSerializationException(
                            "WKB requires every ring of a polygon to use the same coordinate dimensions.");
                return ordinates;
            case GeometryCollection collection:
                Ordinates? common = null;
                for (var index = 0; index < collection.NumGeometries; index++)
                {
                    var child = GetOrdinates(collection.GetGeometryN(index));
                    if (common.HasValue && common != child) return Ordinates.XY;
                    common = child;
                }
                // Collections have no coordinate sequence of their own. Mixed
                // schemas use an XY wrapper; each child declares its own schema.
                return common ?? Ordinates.XY;
            default:
                throw new MessagePackSerializationException($"Unsupported geometry type '{geometry.GeometryType}'.");
        }
    }

    internal static Geometry Read(byte[] bytes)
    {
        // Let NTS validate and decode WKB, including legacy byte order/SRID variants.
        var geometry = new WKBReader().Read(bytes);
        var cursor = new SchemaCursor(bytes);
        return cursor.RestoreEmptySchemas(geometry);
    }

    // NTS 2.6 discards the header's schema when creating empty points/polygons.
    // Walk the already-validated bytes alongside the decoded tree to restore it.
    private ref struct SchemaCursor(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> remaining = bytes;

        internal Geometry RestoreEmptySchemas(Geometry geometry)
        {
            var littleEndian = remaining[0] != (byte)ByteOrder.BigEndian;
            Skip(1);
            var type = ReadUInt32(littleEndian);
            var isoDimension = (type & 0xffff) / 1000;
            // Match NTS: an ISO dimensional prefix takes precedence over EWKB flags.
            var hasZ = isoDimension is 1 or 2 or 3
                ? isoDimension is 1 or 3 : (type & 0x80000000) != 0;
            var hasM = isoDimension is 1 or 2 or 3
                ? isoDimension is 2 or 3 : (type & 0x40000000) != 0;
            var dimension = 2 + (hasZ ? 1 : 0) + (hasM ? 1 : 0);
            var measures = hasM ? 1 : 0;
            if ((type & 0x20000000) != 0) Skip(4);

            switch (geometry)
            {
                case Point point:
                    Skip(dimension * sizeof(double));
                    return point.IsEmpty
                        ? point.Factory.CreatePoint(point.Factory.CoordinateSequenceFactory.Create(0, dimension, measures))
                        : point;
                case LineString:
                    SkipCoordinates(littleEndian, dimension);
                    return geometry;
                case Polygon polygon:
                    var rings = ReadUInt32(littleEndian);
                    for (uint index = 0; index < rings; index++) SkipCoordinates(littleEndian, dimension);
                    return polygon.IsEmpty
                        ? polygon.Factory.CreatePolygon(polygon.Factory.CreateLinearRing(
                            polygon.Factory.CoordinateSequenceFactory.Create(0, dimension, measures)))
                        : polygon;
                case GeometryCollection collection:
                    var count = ReadUInt32(littleEndian);
                    if (count != collection.NumGeometries) throw new ParseException("WKB collection count differs from decoded geometry.");
                    Geometry[]? children = null;
                    for (var index = 0; index < collection.NumGeometries; index++)
                    {
                        var original = collection.GetGeometryN(index);
                        var restored = RestoreEmptySchemas(original);
                        if (!ReferenceEquals(original, restored))
                        {
                            children ??= collection.Geometries.ToArray();
                            children[index] = restored;
                        }
                    }
                    if (children is null) return collection;
                    return collection switch
                    {
                        MultiPoint => collection.Factory.CreateMultiPoint(children.Cast<Point>().ToArray()),
                        MultiLineString => collection.Factory.CreateMultiLineString(children.Cast<LineString>().ToArray()),
                        MultiPolygon => collection.Factory.CreateMultiPolygon(children.Cast<Polygon>().ToArray()),
                        _ => collection.Factory.CreateGeometryCollection(children)
                    };
                default:
                    throw new ParseException($"Unsupported WKB geometry type '{geometry.GeometryType}'.");
            }
        }

        private uint ReadUInt32(bool littleEndian)
        {
            var value = littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(remaining)
                : BinaryPrimitives.ReadUInt32BigEndian(remaining);
            Skip(sizeof(uint));
            return value;
        }

        private void SkipCoordinates(bool littleEndian, int dimension)
            => Skip(checked((int)ReadUInt32(littleEndian) * dimension * sizeof(double)));

        private void Skip(int length) => remaining = remaining[length..];
    }
}
