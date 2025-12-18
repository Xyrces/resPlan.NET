using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;

namespace ResPlan.Library.Data
{
    public class EnvelopeJsonConverter : JsonConverter<Envelope>
    {
        public override Envelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Simple implementation for reading: expect [minX, minY, maxX, maxY]
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                 throw new JsonException("Expected array for Envelope.");
            }

            reader.Read();
            double minX = reader.GetDouble();
            reader.Read();
            double minY = reader.GetDouble();
            reader.Read();
            double maxX = reader.GetDouble();
            reader.Read();
            double maxY = reader.GetDouble();
            reader.Read(); // End array

            return new Envelope(minX, maxX, minY, maxY);
        }

        public override void Write(Utf8JsonWriter writer, Envelope value, JsonSerializerOptions options)
        {
            if (value == null || value.IsNull)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartArray();
            writer.WriteNumberValue(value.MinX);
            writer.WriteNumberValue(value.MinY);
            writer.WriteNumberValue(value.MaxX);
            writer.WriteNumberValue(value.MaxY);
            writer.WriteEndArray();
        }
    }
}
