using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ResPlan.Library;
using ResPlan.Library.Data;

return await Validation.RunAsync(args);

internal static class Validation
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly WKBWriter GeometryWriter = new(ByteOrder.LittleEndian, handleSRID: false, emitZ: false, emitM: false);

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is not (2 or 4) || (args.Length == 4 && args[2] != "--report"))
        {
            Console.Error.WriteLine("Usage: dotnet run --project tools/ResPlan.Validation -c Release -- INPUT.pkl EXPECTED.jsonl [--report SUMMARY.json]");
            return 2;
        }
        try
        {
            var elapsed = Stopwatch.StartNew();
            using var expectedFile = File.OpenText(args[1]);
            using var manifestDocument = JsonDocument.Parse(await expectedFile.ReadLineAsync()
                ?? throw new InvalidDataException("The oracle file is empty."));
            var manifest = manifestDocument.RootElement;
            if (manifest.GetProperty("schema").GetString() != "resplan-native-oracle-v1")
                throw new InvalidDataException("Unsupported oracle schema.");
            var expectedCount = manifest.GetProperty("plan_count").GetInt32();
            var sourceCount = manifest.GetProperty("source_plan_count").GetInt32();
            string digest;
            await using (var stream = File.OpenRead(args[0]))
                digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            if (digest != manifest.GetProperty("input_sha256").GetString())
                throw new InvalidDataException("Input SHA-256 does not match the trusted oracle.");
            var loadWatch = Stopwatch.StartNew();
            var plans = await PlanLoader.LoadPlansAsync(pklPathOverride: args[0], maxItems: expectedCount,
                logger: message => Console.Error.WriteLine(message));
            loadWatch.Stop();
            var errors = new ComparisonErrors();
            if (plans.Count != expectedCount)
                errors.Add("load", $"Expected {expectedCount} plans, received {plans.Count}.");
            long geometryCount = 0, emptyLayers = 0, nodeCount = 0, edgeCount = 0, sourceGraphs = 0, packedBytes = 0;
            var roundTripWatch = new Stopwatch();
            var compareWatch = Stopwatch.StartNew();
            var areaAbsolute = manifest.GetProperty("area_absolute_tolerance").GetDouble();
            var areaRelative = manifest.GetProperty("area_relative_tolerance").GetDouble();
            for (var index = 0; index < expectedCount; index++)
            {
                var line = await expectedFile.ReadLineAsync()
                    ?? throw new InvalidDataException($"Oracle ends before plan {index}.");
                using var expectedDocument = JsonDocument.Parse(line);
                if (expectedDocument.RootElement.GetProperty("id").GetInt32() != index)
                    throw new InvalidDataException($"Oracle plan {index} has an unexpected ordinal ID.");
                if (index >= plans.Count) continue;
                var plan = plans[index];
                var snapshot = JsonSerializer.SerializeToElement(PlanSnapshot(plan), JsonOptions);
                Compare(expectedDocument.RootElement, snapshot, $"plans[{index}]", errors, "oracle", areaAbsolute, areaRelative);

                roundTripWatch.Start();
                try
                {
                    var packed = PlanSerializer.Serialize(plan);
                    packedBytes += packed.Length;
                    var restored = PlanSerializer.Deserialize(packed);
                    var restoredSnapshot = JsonSerializer.SerializeToElement(PlanSnapshot(restored), JsonOptions);
                    // Serialization must retain the exact numeric values, including areas.
                    Compare(snapshot, restoredSnapshot, $"plans[{index}]", errors, "messagepack", 0, 0);
                    // The Python oracle intentionally hashes 2D WKB. Check the complete NTS
                    // geometry schema separately so that dimension or SRID changes cannot hide.
                    ComparePlanGeometries(plan, restored, $"plans[{index}]", errors);
                }
                catch (Exception exception)
                {
                    errors.Add("messagepack", $"plans[{index}]: {exception.GetType().Name}: {exception.Message}");
                }
                finally { roundTripWatch.Stop(); }
                geometryCount += plan.Geometries.Values.Sum(parts => (long)parts.Count);
                emptyLayers += plan.Geometries.Values.Count(parts => parts.Count == 0);
                nodeCount += plan.ReferenceGraph?.Nodes.Count ?? 0;
                edgeCount += plan.ReferenceGraph?.Edges.Count ?? 0;
                sourceGraphs += plan.SourceGraph is null ? 0 : 1;
                if ((index + 1) % 1000 == 0)
                    Console.Error.WriteLine($"Validated {index + 1}/{expectedCount} plans; errors={errors.Count}; elapsed={elapsed.Elapsed.TotalSeconds:F1}s");
            }
            compareWatch.Stop();
            if (await expectedFile.ReadLineAsync() is not null)
                throw new InvalidDataException("Oracle contains extra plan records beyond its manifest count.");
            var report = new
            {
                success = errors.Count == 0,
                input_sha256 = digest,
                source_plan_count = sourceCount,
                plans_validated = plans.Count,
                geometry_parts = geometryCount,
                empty_geometry_layers = emptyLayers,
                generated_graph_nodes = nodeCount,
                generated_graph_edges = edgeCount,
                source_graphs = sourceGraphs,
                messagepack_roundtrips = plans.Count,
                messagepack_bytes = packedBytes,
                errors = errors.Count,
                errors_by_stage = errors.CountByStage,
                first_errors = errors.Examples,
                area_absolute_tolerance = areaAbsolute,
                area_relative_tolerance = areaRelative,
                native_load_seconds = loadWatch.Elapsed.TotalSeconds,
                comparison_and_roundtrip_seconds = compareWatch.Elapsed.TotalSeconds,
                messagepack_roundtrip_seconds = roundTripWatch.Elapsed.TotalSeconds,
                total_seconds = elapsed.Elapsed.TotalSeconds,
                peak_working_set_bytes = Process.GetCurrentProcess().PeakWorkingSet64
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            if (args.Length == 4) await File.WriteAllTextAsync(args[3], json + Environment.NewLine);
            return errors.Count == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static object PlanSnapshot(Plan plan) => new
    {
        id = plan.Id,
        geometries = plan.Geometries.ToDictionary(pair => pair.Key, pair => pair.Value.Select(GeometryHash).ToArray()),
        metadata = Metadata(plan.Metadata),
        bounds = plan.Bounds is null || plan.Bounds.IsNull ? null : new[] { plan.Bounds.MinX, plan.Bounds.MinY, plan.Bounds.MaxX, plan.Bounds.MaxY },
        wall_width = Metadata(plan.WallWidth),
        reference_graph = GraphSnapshot(plan.ReferenceGraph),
        source_graph = GraphSnapshot(plan.SourceGraph)
    };

    private static object? GraphSnapshot(Graph? graph)
    {
        if (graph is null) return null;
        return new
        {
            nodes = graph.Nodes.ToDictionary(pair => pair.Key, pair => new
            {
                id = pair.Value.Id,
                type = pair.Value.Type,
                area = Metadata(pair.Value.Area),
                geometry = GeometryHash(pair.Value.Geometry),
                metadata = Metadata(pair.Value.Metadata)
            }),
            edges = graph.Edges.Select(edge => new
            {
                source = string.CompareOrdinal(edge.SourceId, edge.TargetId) <= 0 ? edge.SourceId : edge.TargetId,
                target = string.CompareOrdinal(edge.SourceId, edge.TargetId) <= 0 ? edge.TargetId : edge.SourceId,
                type = edge.Type,
                metadata = Metadata(edge.Metadata)
            }).OrderBy(edge => edge.source, StringComparer.Ordinal)
                .ThenBy(edge => edge.target, StringComparer.Ordinal)
                .ThenBy(edge => edge.type, StringComparer.Ordinal).ToArray(),
            metadata = Metadata(graph.Metadata)
        };
    }

    private static string? GeometryHash(Geometry? geometry) => geometry is null ? null
        : Convert.ToHexStringLower(SHA256.HashData(GeometryWriter.Write(geometry)));

    private static void ComparePlanGeometries(Plan expected, Plan actual, string path, ComparisonErrors errors)
    {
        // Missing layers, nodes, and geometry parts are already reported by Compare above.
        foreach (var (layer, parts) in expected.Geometries)
            if (actual.Geometries.TryGetValue(layer, out var restoredParts))
                for (var index = 0; index < Math.Min(parts.Count, restoredParts.Count); index++)
                    CompareGeometry(parts[index], restoredParts[index], $"{path}.geometries[{layer}][{index}]", errors);
        CompareGraphGeometries(expected.ReferenceGraph, actual.ReferenceGraph, path + ".reference_graph", errors);
        CompareGraphGeometries(expected.SourceGraph, actual.SourceGraph, path + ".source_graph", errors);
    }

    private static void CompareGraphGeometries(Graph? expected, Graph? actual, string path, ComparisonErrors errors)
    {
        if (expected is null || actual is null) return;
        foreach (var (id, node) in expected.Nodes)
            if (actual.Nodes.TryGetValue(id, out var restoredNode))
                CompareGeometry(node.Geometry, restoredNode.Geometry, $"{path}.nodes[{id}].geometry", errors);
    }

    private static void CompareGeometry(Geometry? expected, Geometry? actual, string path, ComparisonErrors errors)
    {
        if (expected is null || actual is null)
        {
            if (expected is not null || actual is not null)
                errors.Add("messagepack", $"{path}: geometry nullability changed");
            return;
        }
        if (expected.GeometryType != actual.GeometryType)
        {
            errors.Add("messagepack", $"{path}: expected {expected.GeometryType}, actual {actual.GeometryType}");
            return;
        }
        if (expected.SRID != actual.SRID)
            errors.Add("messagepack", $"{path}.srid: expected {expected.SRID}, actual {actual.SRID}");
        switch (expected)
        {
            case Point point:
                CompareCoordinates(point.CoordinateSequence, ((Point)actual).CoordinateSequence, path, errors);
                break;
            case LineString line:
                CompareCoordinates(line.CoordinateSequence, ((LineString)actual).CoordinateSequence, path, errors);
                break;
            case Polygon polygon:
                var restoredPolygon = (Polygon)actual;
                CompareGeometry(polygon.ExteriorRing, restoredPolygon.ExteriorRing, path + ".shell", errors);
                if (polygon.NumInteriorRings != restoredPolygon.NumInteriorRings)
                    errors.Add("messagepack", $"{path}: expected {polygon.NumInteriorRings} holes, actual {restoredPolygon.NumInteriorRings}");
                for (var index = 0; index < Math.Min(polygon.NumInteriorRings, restoredPolygon.NumInteriorRings); index++)
                    CompareGeometry(polygon.GetInteriorRingN(index), restoredPolygon.GetInteriorRingN(index), $"{path}.holes[{index}]", errors);
                break;
            case GeometryCollection collection:
                var restoredCollection = (GeometryCollection)actual;
                if (collection.NumGeometries != restoredCollection.NumGeometries)
                    errors.Add("messagepack", $"{path}: expected {collection.NumGeometries} children, actual {restoredCollection.NumGeometries}");
                for (var index = 0; index < Math.Min(collection.NumGeometries, restoredCollection.NumGeometries); index++)
                    CompareGeometry(collection.GetGeometryN(index), restoredCollection.GetGeometryN(index), $"{path}.children[{index}]", errors);
                break;
            default:
                throw new InvalidDataException($"{path}: unsupported geometry type {expected.GeometryType}");
        }
    }

    private static void CompareCoordinates(CoordinateSequence expected, CoordinateSequence actual, string path, ComparisonErrors errors)
    {
        if (expected.Count != actual.Count || expected.Dimension != actual.Dimension || expected.Measures != actual.Measures)
            errors.Add("messagepack", $"{path}.coordinates: expected count/dimension/measures {expected.Count}/{expected.Dimension}/{expected.Measures}, actual {actual.Count}/{actual.Dimension}/{actual.Measures}");
        for (var coordinate = 0; coordinate < Math.Min(expected.Count, actual.Count); coordinate++)
            for (var ordinate = 0; ordinate < Math.Min(expected.Dimension, actual.Dimension); ordinate++)
            {
                var expectedBits = BitConverter.DoubleToInt64Bits(expected.GetOrdinate(coordinate, ordinate));
                var actualBits = BitConverter.DoubleToInt64Bits(actual.GetOrdinate(coordinate, ordinate));
                if (expectedBits != actualBits)
                    errors.Add("messagepack", $"{path}.coordinates[{coordinate}][{ordinate}]: expected bits 0x{expectedBits:X16}, actual 0x{actualBits:X16}");
            }
    }

    private static object? Metadata(object? value)
    {
        if (value is double number && !double.IsFinite(number))
            return new Dictionary<string, string> { ["$float"] = double.IsNaN(number) ? "nan" : number > 0 ? "inf" : "-inf" };
        if (value is float single && !float.IsFinite(single)) return Metadata((double)single);
        if (value is byte[] bytes) return new Dictionary<string, string> { ["$bytes"] = Convert.ToBase64String(bytes) };
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key) throw new InvalidDataException("Metadata has a nonstring dictionary key.");
                result.Add(key, Metadata(entry.Value));
            }
            return result;
        }
        if (value is IEnumerable sequence && value is not string)
            return sequence.Cast<object?>().Select(Metadata).ToArray();
        return value;
    }

    internal static void Compare(JsonElement expected, JsonElement actual, string path, ComparisonErrors errors,
        string stage, double areaAbsolute, double areaRelative, ComparisonValue context = ComparisonValue.Plan)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            errors.Add(stage, $"{path}: expected {expected.ValueKind}, actual {actual.ValueKind}");
            return;
        }
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in expected.EnumerateObject())
                {
                    if (!actual.TryGetProperty(property.Name, out var actualValue))
                        errors.Add(stage, $"{path}.{property.Name}: missing property");
                    else Compare(property.Value, actualValue, path + "." + property.Name, errors, stage,
                        areaAbsolute, areaRelative, ChildContext(context, property.Name));
                }
                foreach (var property in actual.EnumerateObject())
                    if (!expected.TryGetProperty(property.Name, out _)) errors.Add(stage, $"{path}.{property.Name}: unexpected property");
                break;
            case JsonValueKind.Array:
                if (expected.GetArrayLength() != actual.GetArrayLength())
                    errors.Add(stage, $"{path}: expected {expected.GetArrayLength()} elements, actual {actual.GetArrayLength()}");
                for (var index = 0; index < Math.Min(expected.GetArrayLength(), actual.GetArrayLength()); index++)
                    Compare(expected[index], actual[index], $"{path}[{index}]", errors, stage,
                        areaAbsolute, areaRelative, ComparisonValue.Normal);
                break;
            case JsonValueKind.Number:
                // Compare numeric JSON representations without decimal or double rounding.
                var equal = JsonElement.DeepEquals(expected, actual);
                if (!equal && context == ComparisonValue.Area && (areaAbsolute > 0 || areaRelative > 0))
                {
                    var expectedNumber = expected.GetDouble();
                    var actualNumber = actual.GetDouble();
                    equal = double.IsFinite(expectedNumber) && double.IsFinite(actualNumber)
                        && Math.Abs(expectedNumber - actualNumber)
                        <= areaAbsolute + areaRelative * Math.Max(Math.Abs(expectedNumber), Math.Abs(actualNumber));
                }
                if (!equal) errors.Add(stage, $"{path}: expected {expected.GetRawText()}, actual {actual.GetRawText()}");
                break;
            case JsonValueKind.String:
                if (expected.GetString() != actual.GetString())
                    errors.Add(stage, $"{path}: expected {expected.GetRawText()}, actual {actual.GetRawText()}");
                break;
        }
    }

    internal enum ComparisonValue { Normal, Plan, Graph, Nodes, Node, Area }

    private static ComparisonValue ChildContext(ComparisonValue parent, string key) => parent switch
    {
        ComparisonValue.Plan when key is "reference_graph" or "source_graph" => ComparisonValue.Graph,
        ComparisonValue.Graph when key == "nodes" => ComparisonValue.Nodes,
        ComparisonValue.Nodes => ComparisonValue.Node,
        ComparisonValue.Node when key == "area" => ComparisonValue.Area,
        _ => ComparisonValue.Normal
    };

    internal sealed class ComparisonErrors
    {
        internal long Count { get; private set; }
        internal Dictionary<string, long> CountByStage { get; } = new();
        internal List<string> Examples { get; } = new();

        internal void Add(string stage, string message)
        {
            Count++;
            CountByStage[stage] = CountByStage.GetValueOrDefault(stage) + 1;
            if (Examples.Count >= 30) return;
            Examples.Add($"{stage}: {message}");
            Console.Error.WriteLine($"Mismatch ({stage}): {message}");
        }
    }
}
