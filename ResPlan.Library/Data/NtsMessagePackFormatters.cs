using MessagePack;
using MessagePack.Formatters;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Buffers;

namespace ResPlan.Library.Data
{
    public class GeometryMessagePackFormatter : GeometryMessagePackFormatter<Geometry>
    {
    }

    public class GeometryMessagePackFormatter<TGeometry> : IMessagePackFormatter<TGeometry?>
        where TGeometry : Geometry
    {
        public void Serialize(ref MessagePackWriter writer, TGeometry? value, MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            // Keep the existing binary representation, using EWKB to retain SRID,
            // elevation and measure values. NTS codecs are not thread-safe.
            var bytes = new WKBWriter(ByteOrder.LittleEndian, handleSRID: true, emitZ: true, emitM: true).Write(value);
            writer.Write(bytes);
        }

        public TGeometry? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return null;
            }

            var bytes = reader.ReadBytes();
            if (bytes == null) return null;

            // WKBReader accepts both legacy two-dimensional WKB and EWKB.
            Geometry geometry = new WKBReader().Read(bytes.Value.ToArray());
            // WKB represents a standalone LinearRing as a LineString. Its declared
            // managed type supplies the missing distinction when reading a ring.
            if (typeof(TGeometry) == typeof(LinearRing) && geometry is LineString line)
                geometry = line.Factory.CreateLinearRing(line.CoordinateSequence);

            return geometry as TGeometry ?? throw new MessagePackSerializationException(
                $"Expected {typeof(TGeometry).Name} geometry, but the payload contains {geometry.GeometryType}.");
        }
    }

    public class EnvelopeMessagePackFormatter : IMessagePackFormatter<Envelope?>
    {
        public void Serialize(ref MessagePackWriter writer, Envelope? value, MessagePackSerializerOptions options)
        {
            if (value == null || value.IsNull)
            {
                writer.WriteNil();
                return;
            }

            // Serialize as array [minX, maxX, minY, maxY]
            writer.WriteArrayHeader(4);
            writer.Write(value.MinX);
            writer.Write(value.MaxX);
            writer.Write(value.MinY);
            writer.Write(value.MaxY);
        }

        public Envelope? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return null;
            }

            options.Security.DepthStep(ref reader);
            try
            {
                var count = reader.ReadArrayHeader();
                if (count != 4)
                    throw new MessagePackSerializationException("Invalid envelope array length");

                var minX = reader.ReadDouble();
                var maxX = reader.ReadDouble();
                var minY = reader.ReadDouble();
                var maxY = reader.ReadDouble();

                return new Envelope(minX, maxX, minY, maxY);
            }
            finally
            {
                reader.Depth--;
            }
        }
    }

    public class ResPlanMessagePackResolver : IFormatterResolver
    {
        public static readonly IFormatterResolver Instance = new ResPlanMessagePackResolver();

        // Expose options for convenience
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
            .WithResolver(Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData);

        private static readonly IFormatterResolver[] Resolvers = new IFormatterResolver[]
        {
            // Custom resolvers first
            new NtsFormatterResolver(),
            // Standard resolver
            MessagePack.Resolvers.StandardResolver.Instance
        };

        public IMessagePackFormatter<T>? GetFormatter<T>()
        {
            return FormatterCache<T>.Formatter;
        }

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T>? Formatter;

            static FormatterCache()
            {
                foreach (var resolver in Resolvers)
                {
                    var f = resolver.GetFormatter<T>();
                    if (f != null)
                    {
                        Formatter = f;
                        return;
                    }
                }
            }
        }
    }

    internal class NtsFormatterResolver : IFormatterResolver
    {
        public IMessagePackFormatter<T>? GetFormatter<T>() => FormatterCache<T>.Formatter;

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T>? Formatter = CreateFormatter();

            private static IMessagePackFormatter<T>? CreateFormatter()
            {
                if (typeof(Geometry).IsAssignableFrom(typeof(T)))
                    return (IMessagePackFormatter<T>)Activator.CreateInstance(
                        typeof(GeometryMessagePackFormatter<>).MakeGenericType(typeof(T)))!;
                if (typeof(T) == typeof(Envelope))
                    return (IMessagePackFormatter<T>)(object)new EnvelopeMessagePackFormatter();
                return null;
            }
        }
    }
}
