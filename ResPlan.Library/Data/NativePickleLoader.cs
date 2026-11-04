using System.Collections;
using System.Globalization;
using System.Numerics;
using NetTopologySuite.Geometries;
using Pickle.Net;
using ResPlan.Library.Data.PickleAdapters;

namespace ResPlan.Library.Data;

/// <summary>Reads ResPlan pickle data using a bounded, explicitly registered native decoder.</summary>
internal static class NativePickleLoader
{
    internal const int DefaultMaxMemoEntries = 2_000_000;
    internal const long DefaultMaxInputBytes = 512L * 1024 * 1024;

    internal static List<Plan> Load(string path, int? maxItems = null,
        CancellationToken cancellationToken = default, int maxMemoEntries = DefaultMaxMemoEntries,
        long maxInputBytes = DefaultMaxInputBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return Load(stream, maxItems, cancellationToken, maxMemoEntries, maxInputBytes);
    }

    /// <remarks>The caller retains ownership of the stream. A limit selects plans after decoding the pickle object graph.</remarks>
    internal static List<Plan> Load(Stream stream, int? maxItems = null,
        CancellationToken cancellationToken = default, int maxMemoEntries = DefaultMaxMemoEntries,
        long maxInputBytes = DefaultMaxInputBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxItems < 0) throw new ArgumentOutOfRangeException(nameof(maxItems));
        if (maxMemoEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxMemoEntries));
        if (maxInputBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanRead) throw new ArgumentException("The pickle stream must be readable.", nameof(stream));
        if (stream.CanSeek && stream.Length - stream.Position > maxInputBytes)
            throw new InvalidDataException($"The ResPlan pickle exceeds the {maxInputBytes}-byte input limit.");
        if (maxItems == 0) return [];

        using var input = new BoundedReadStream(stream, cancellationToken, maxInputBytes);
        object? decoded;
        try
        {
            decoded = PickleSerializer.Load(input, new UnpicklerOptions
            {
                Registry = ResPlanPickleRegistry.Create(),
                MaxMemoEntries = maxMemoEntries
            });
        }
        catch (PickleException exception)
        {
            throw new InvalidDataException($"Cannot decode ResPlan pickle: {exception.Message}", exception);
        }
        if (decoded is not IList records)
            throw new InvalidDataException("A ResPlan pickle must contain a list of plan dictionaries.");

        var count = Math.Min(records.Count, maxItems ?? records.Count);
        var plans = new List<Plan>(count);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (records[index] is not IDictionary record)
                throw new InvalidDataException($"ResPlan item {index} is not a plan dictionary.");
            try { plans.Add(ConvertPlan(record, index, cancellationToken)); }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or FormatException or OverflowException or TopologyException)
            { throw new InvalidDataException($"Cannot convert ResPlan item {index}: {exception.Message}", exception); }
        }
        return plans;
    }

    private static Plan ConvertPlan(IDictionary record, int index, CancellationToken cancellationToken)
    {
        var plan = new Plan { Id = index };
        foreach (DictionaryEntry entry in record)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Key is not string key)
                throw new InvalidDataException("Plan dictionary keys must be strings.");
            if (key == "graph")
            {
                if (entry.Value is NetworkXGraph graph) plan.SourceGraph = ConvertGraph(graph);
                else if (entry.Value is not null) throw new InvalidDataException("The plan graph is not a supported NetworkX Graph.");
                continue;
            }
            if (TryGeometries(entry.Value, out var geometries))
            {
                // Match normalize_keys: rename the known typo only when the canonical key is absent.
                var category = key == "balacony" && !record.Contains("balcony") ? "balcony" : key;
                plan.Geometries[category] = geometries;
            }
            else
            {
                plan.Metadata[key] = NormalizeMetadata(entry.Value);
            }
        }
        if (!plan.Geometries.ContainsKey("stairs") && plan.Geometries.TryGetValue("stair", out var stairs))
            plan.Geometries["stairs"] = stairs;
        if (record.Contains("wall_width") && record["wall_width"] is not null)
            plan.WallWidth = Number(record["wall_width"], "wall_width");
        plan.Bounds = GeometryLayers.GetBounds(GeometryLayers.GetFootprint(plan.Geometries));
        // The former Python loader deliberately regenerated this graph instead of using a stored graph.
        // Preserve the stored version separately, including attributes, for callers that need it.
        plan.ReferenceGraph = GraphGenerator.GenerateGraph(plan);
        return plan;
    }

    private static bool TryGeometries(object? value, out List<Geometry> geometries)
    {
        geometries = [];
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return CollectGeometries(value, geometries, seen, 0);
    }

    private static bool CollectGeometries(object? value, List<Geometry> geometries, HashSet<object> seen, int depth)
    {
        if (depth > 256) throw new InvalidDataException("Geometry collections are nested too deeply.");
        if (value is Geometry geometry)
        {
            if (geometry.IsEmpty) return true;
            if (geometry is GeometryCollection collection)
            {
                for (var index = 0; index < collection.NumGeometries; index++)
                    CollectGeometries(collection.GetGeometryN(index), geometries, seen, depth + 1);
            }
            else geometries.Add(geometry);
            return true;
        }
        if (value is not (IList or PickleTuple)) return false;
        if (!seen.Add(value)) throw new InvalidDataException("A geometry sequence contains a cycle.");
        try
        {
            var hasGeometry = false;
            foreach (var item in (IEnumerable)value)
            {
                if (item is null) continue;
                if (!CollectGeometries(item, geometries, seen, depth + 1)) return false;
                hasGeometry = true;
            }
            return hasGeometry;
        }
        finally { seen.Remove(value); }
    }

    private static Graph ConvertGraph(NetworkXGraph source)
    {
        var graph = new Graph();
        if (source.State["graph"] is IDictionary attributes)
            CopyAttributes(attributes, graph.Metadata);
        else if (source.State["graph"] is not null)
            throw new InvalidDataException("NetworkX graph attributes must be a dictionary.");
        var nodes = source.State["_node"] as IDictionary;
        if (nodes is null && source.State["nodes"] is NetworkXNodeView view)
            nodes = view.State["_nodes"] as IDictionary;
        if (nodes is null) throw new InvalidDataException("NetworkX Graph is missing its node dictionary.");

        foreach (DictionaryEntry entry in nodes)
        {
            var id = NodeId(entry.Key);
            if (entry.Value is not IDictionary nodeAttributes)
                throw new InvalidDataException($"NetworkX node '{id}' has invalid attributes.");
            var node = new Node { Id = id, Type = AttributeString(nodeAttributes["type"]) };
            if (nodeAttributes["geometry"] is Geometry geometry) node.Geometry = geometry;
            else if (nodeAttributes["geometry"] is not null)
                throw new InvalidDataException($"NetworkX node '{id}' has an unsupported geometry value.");
            node.Area = nodeAttributes["area"] is null ? node.Geometry?.Area ?? 0 : Number(nodeAttributes["area"], $"node '{id}' area");
            CopyAttributes(nodeAttributes, node.Metadata, "geometry", "type", "area");
            if (!graph.Nodes.TryAdd(id, node)) throw new InvalidDataException($"NetworkX node IDs collide after conversion: '{id}'.");
        }

        if (source.State["_adj"] is not IDictionary adjacency)
            throw new InvalidDataException("NetworkX Graph is missing its adjacency dictionary.");
        var visited = new HashSet<(string, string)>();
        foreach (DictionaryEntry sourceEntry in adjacency)
        {
            var sourceId = NodeId(sourceEntry.Key);
            if (!graph.Nodes.ContainsKey(sourceId) || sourceEntry.Value is not IDictionary neighbors)
                throw new InvalidDataException($"NetworkX adjacency for '{sourceId}' is invalid.");
            foreach (DictionaryEntry targetEntry in neighbors)
            {
                var targetId = NodeId(targetEntry.Key);
                if (!graph.Nodes.ContainsKey(targetId) || targetEntry.Value is not IDictionary edgeAttributes)
                    throw new InvalidDataException($"NetworkX edge '{sourceId}'—'{targetId}' is invalid.");
                var pair = string.CompareOrdinal(sourceId, targetId) <= 0 ? (sourceId, targetId) : (targetId, sourceId);
                if (!visited.Add(pair)) continue;
                var edge = new Edge { SourceId = sourceId, TargetId = targetId, Type = AttributeString(edgeAttributes["type"]) };
                CopyAttributes(edgeAttributes, edge.Metadata, "type");
                graph.Edges.Add(edge);
            }
        }
        return graph;
    }

    private static string AttributeString(object? value) => value is null ? "" : NodeId(value);

    private static string NodeId(object? value) => value switch
    {
        string text => text,
        bool boolean => boolean ? "True" : "False",
        null => "None",
        float number => FormatPythonFloat(number),
        double number => FormatPythonFloat(number),
        sbyte or byte or short or ushort or int or uint or long or ulong or BigInteger
            => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => throw new InvalidDataException($"Unsupported NetworkX identifier type '{value.GetType().Name}'.")
    };

    private static string FormatPythonFloat(double value)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        var sign = double.IsNegative(value) ? "-" : "";
        if (value == 0) return sign + "0.0";

        // Retain the shortest round-trip digits, but apply Python str(float)'s
        // notation thresholds, exponent spelling, and mandatory .0 for integers.
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOf('E');
        var exponent = exponentIndex < 0 ? 0 : int.Parse(text.AsSpan(exponentIndex + 1), CultureInfo.InvariantCulture);
        var significand = exponentIndex < 0 ? text : text[..exponentIndex];
        var point = significand.IndexOf('.');
        var digits = significand.Replace(".", "", StringComparison.Ordinal);
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        var decimalPosition = (point < 0 ? significand.Length : point) + exponent - leadingZeros;
        digits = digits[leadingZeros..].TrimEnd('0');
        exponent = decimalPosition - 1;

        if (exponent is < -4 or >= 16)
        {
            var mantissa = digits.Length == 1 ? digits : digits.Insert(1, ".");
            return sign + mantissa + "e" + (exponent < 0 ? "-" : "+")
                + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        }
        if (decimalPosition <= 0)
            return sign + "0." + new string('0', -decimalPosition) + digits;
        if (decimalPosition >= digits.Length)
            return sign + digits + new string('0', decimalPosition - digits.Length) + ".0";
        return sign + digits.Insert(decimalPosition, ".");
    }

    private static double Number(object? value, string name) => value switch
    {
        bool boolean => boolean ? 1 : 0,
        BigInteger integer => (double)integer,
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double
            => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException($"'{name}' must be numeric.")
    };

    private static void CopyAttributes(IDictionary source, Dictionary<string, object?> target, params string[] excluded)
    {
        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is not string key) throw new InvalidDataException("Attribute dictionary keys must be strings.");
            if (!excluded.Contains(key, StringComparer.Ordinal)) target[key] = NormalizeMetadata(entry.Value);
        }
    }

    private static object? NormalizeMetadata(object? value)
        => NormalizeMetadata(value, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);

    private static object? NormalizeMetadata(object? value, HashSet<object> ancestors, int depth)
    {
        if (depth > 256) throw new InvalidDataException("Metadata is nested too deeply.");
        if (value is null or bool or string or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or byte[])
            return value;
        if (value is PickleByteArray byteArray) return byteArray.Data;
        if (value is PickleBuffer buffer) return buffer.Data.ToArray();
        if (value is BigInteger integer)
        {
            if (integer >= long.MinValue && integer <= long.MaxValue) return (long)integer;
            if (integer >= 0 && integer <= ulong.MaxValue) return (ulong)integer;
            throw new InvalidDataException("A metadata integer exceeds the supported 64-bit range.");
        }
        if (!ancestors.Add(value)) throw new InvalidDataException("Metadata contains a reference cycle.");
        try
        {
            if (value is IDictionary dictionary)
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key) throw new InvalidDataException("Metadata dictionary keys must be strings.");
                    result.Add(key, NormalizeMetadata(entry.Value, ancestors, depth + 1));
                }
                return result;
            }
            if (value is IList or PickleTuple)
                return ((IEnumerable)value).Cast<object?>().Select(item => NormalizeMetadata(item, ancestors, depth + 1)).ToArray();
            throw new InvalidDataException($"Unsupported metadata type '{value.GetType().Name}'; the value was not discarded.");
        }
        finally { ancestors.Remove(value); }
    }

    private sealed class BoundedReadStream(Stream source, CancellationToken cancellationToken, long maxInputBytes) : Stream
    {
        private long bytesRead;
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => bytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer);
            bytesRead += read;
            if (bytesRead > maxInputBytes) throw new InvalidDataException($"The ResPlan pickle exceeds the {maxInputBytes}-byte input limit.");
            return read;
        }
        public override int ReadByte()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = source.ReadByte();
            if (value >= 0 && ++bytesRead > maxInputBytes)
                throw new InvalidDataException($"The ResPlan pickle exceeds the {maxInputBytes}-byte input limit.");
            return value;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
