using MessagePack;
using MessagePack.Formatters;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Buffers;
using System.IO;

namespace ResPlan.Library.Data
{
    public class GeometryMessagePackFormatter : IMessagePackFormatter<Geometry>
    {
        private static readonly WKBWriter _writer = new WKBWriter();
        private static readonly WKBReader _reader = new WKBReader();

        public void Serialize(ref MessagePackWriter writer, Geometry value, MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            // Serialize as WKB byte array
            var bytes = _writer.Write(value);
            writer.Write(bytes);
        }

        public Geometry Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return null;
            }

            var bytes = reader.ReadBytes();
            if (bytes == null) return null;

            return _reader.Read(bytes.Value.ToArray());
        }
    }

    public class EnvelopeMessagePackFormatter : IMessagePackFormatter<Envelope>
    {
        public void Serialize(ref MessagePackWriter writer, Envelope value, MessagePackSerializerOptions options)
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

        public Envelope Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return null;
            }

            var count = reader.ReadArrayHeader();
            if (count != 4)
            {
                 throw new MessagePackSerializationException("Invalid envelope array length");
            }

            var minX = reader.ReadDouble();
            var maxX = reader.ReadDouble();
            var minY = reader.ReadDouble();
            var maxY = reader.ReadDouble();

            return new Envelope(minX, maxX, minY, maxY);
        }
    }

    public class ResPlanMessagePackResolver : IFormatterResolver
    {
        public static readonly IFormatterResolver Instance = new ResPlanMessagePackResolver();

        // Expose options for convenience
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithResolver(Instance);

        private static readonly IFormatterResolver[] Resolvers = new IFormatterResolver[]
        {
            // Custom resolvers first
            new NtsFormatterResolver(),
            // Standard resolver
            MessagePack.Resolvers.StandardResolver.Instance
        };

        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            return FormatterCache<T>.Formatter;
        }

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;

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
        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            if (typeof(T) == typeof(Geometry) || typeof(T).IsSubclassOf(typeof(Geometry)))
            {
                return (IMessagePackFormatter<T>)new GeometryMessagePackFormatter();
            }
            if (typeof(T) == typeof(Envelope))
            {
                return (IMessagePackFormatter<T>)new EnvelopeMessagePackFormatter();
            }
            return null;
        }
    }
}
