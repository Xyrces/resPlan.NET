using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ResPlan.Library.Data;

namespace ResPlan.Library;

public class PlanLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <summary>Load legacy JSON or a ResPlan pickle using managed code only.</summary>
    /// <remarks>maxItems limits conversion before constraints; pickle decoding still reads the complete object graph.</remarks>
    public static Task<List<Plan>> LoadPlansAsync(string? jsonPath = null,
        string? pklPathOverride = null, int? maxItems = null, Action<string>? logger = null,
        PlanGenerationConstraints? constraints = null) =>
        LoadPlansAsync(CancellationToken.None, jsonPath, pklPathOverride, maxItems, logger, constraints);

    public static async Task<List<Plan>> LoadPlansAsync(CancellationToken cancellationToken,
        string? jsonPath = null, string? pklPathOverride = null, int? maxItems = null,
        Action<string>? logger = null, PlanGenerationConstraints? constraints = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxItems ?? 0);
        cancellationToken.ThrowIfCancellationRequested();
        if (maxItems == 0) return new();

        List<Plan> plans;
        if (!string.IsNullOrEmpty(jsonPath))
        {
            var json = await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false);
            plans = ParseJson(json, maxItems);
        }
        else
        {
            if (string.IsNullOrEmpty(pklPathOverride))
            {
                await DataManager.EnsureDataAsync(logger, cancellationToken).ConfigureAwait(false);
                pklPathOverride = DataManager.GetDataPath();
            }
            plans = await Task.Run(() =>
            {
                logger?.Invoke($"Loading {pklPathOverride} with Pickle.Net...");
                return NativePickleLoader.Load(pklPathOverride, maxItems, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (constraints?.BoundingPolygon is not null)
        {
            var compatible = new List<Plan>();
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsPlanCompatible(plan, constraints.BoundingPolygon)) compatible.Add(plan);
            }
            plans = compatible;
        }
        logger?.Invoke($"Loaded {plans.Count} plans.");
        return plans;
    }

    public static List<Plan> LoadPlans(string jsonPath) => ParseJson(File.ReadAllText(jsonPath), null);

    internal static bool IsPlanCompatible(Plan plan, Polygon? boundingPolygon)
    {
        if (boundingPolygon is null) return true;
        var footprint = GeometryLayers.GetFootprint(plan.Geometries);
        // Bounds from JSON or a mutable Plan may be stale or describe a rendering viewport.
        // Derive the fast check from the same geometry used by the precise fallback.
        var bounds = GeometryLayers.GetBounds(footprint);
        if (!bounds.IsNull && boundingPolygon.Covers(boundingPolygon.Factory.ToGeometry(bounds)))
            return true;
        return footprint.All(boundingPolygon.Covers);
    }

    private static List<Plan> ParseJson(string json, int? maxItems)
    {
        // Older Python exports emitted bare NaN/Infinity. Quote only standalone tokens,
        // leaving property names, escaped strings and valid numeric values unchanged.
        var data = JsonSerializer.Deserialize<List<ResPlanData>>(QuoteNamedNumbers(json), JsonOptions)
            ?? throw new JsonException("Expected an array of plans.");
        return data.Take(maxItems ?? int.MaxValue).Select(ConvertDataToPlan).ToList();
    }

    private static Plan ConvertDataToPlan(ResPlanData data)
    {
        var reader = new WKTReader();
        var plan = new Plan
        {
            Id = data.Id,
            WallWidth = double.IsFinite(data.WallWidth) ? data.WallWidth : 0.1,
            Metadata = NormalizeMetadata(data.Metadata),
            ReferenceGraph = ConvertGraph(data.ReferenceGraph),
            SourceGraph = ConvertGraph(data.SourceGraph)
        };
        foreach (var (category, wkts) in data.Geometries ?? new())
            plan.Geometries[category] = (wkts ?? new()).Select(reader.Read).ToList();
        if (plan.Geometries.TryGetValue("stair", out var stairs))
            plan.Geometries.TryAdd("stairs", stairs);
        if (plan.Geometries.TryGetValue("balacony", out var balcony))
            plan.Geometries.TryAdd("balcony", balcony);

        if (data.Bounds is { Length: 4 } bounds)
        {
            if (bounds.All(double.IsFinite))
                plan.Bounds = new Envelope(bounds[0], bounds[2], bounds[1], bounds[3]);
        }
        else
        {
            plan.Bounds = GeometryLayers.GetBounds(GeometryLayers.GetFootprint(plan.Geometries));
        }
        return plan;
    }

    private static Graph? ConvertGraph(GraphData? data)
    {
        if (data is null) return null;
        var graph = new Graph { Metadata = NormalizeMetadata(data.Metadata) };
        foreach (var node in data.Nodes ?? new())
        {
            var area = node.Area ?? 0;
            graph.Nodes.Add(node.Id, new Node
            {
                Id = node.Id, Type = node.Type, Area = double.IsFinite(area) ? area : 0,
                Metadata = NormalizeMetadata(node.Metadata)
            });
        }
        foreach (var edge in data.Edges ?? new())
            graph.Edges.Add(new Edge
            {
                SourceId = edge.Source, TargetId = edge.Target, Type = edge.Type,
                Metadata = NormalizeMetadata(edge.Metadata)
            });
        return graph;
    }

    private static Dictionary<string, object?> NormalizeMetadata(Dictionary<string, object?>? values) =>
        values?.ToDictionary(pair => pair.Key, pair => NormalizeJson(pair.Value)) ?? new();

    private static object? NormalizeJson(object? value) => value is not JsonElement json ? value : json.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => json.GetString(),
        JsonValueKind.Number when json.TryGetInt64(out var number) => number,
        JsonValueKind.Number when json.TryGetUInt64(out var number) => number,
        JsonValueKind.Number => json.GetDouble(),
        JsonValueKind.Array => json.EnumerateArray().Select(item => NormalizeJson(item)).ToArray(),
        JsonValueKind.Object => json.EnumerateObject().ToDictionary(item => item.Name, item => NormalizeJson(item.Value)),
        _ => throw new JsonException("Unsupported metadata value.")
    };

    private static string QuoteNamedNumbers(string json)
    {
        var result = new StringBuilder(json.Length);
        bool inString = false, escaped = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                result.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; result.Append(c); continue; }
            string? token = null;
            if (i == 0 || char.IsWhiteSpace(json[i - 1]) || json[i - 1] is ':' or ',' or '[')
                foreach (var candidate in new[] { "NaN", "Infinity", "-Infinity" })
                    if (json.AsSpan(i).StartsWith(candidate, StringComparison.Ordinal)
                        && (i + candidate.Length == json.Length || char.IsWhiteSpace(json[i + candidate.Length])
                            || json[i + candidate.Length] is ',' or ']' or '}'))
                    { token = candidate; break; }
            if (token is null) result.Append(c);
            else { result.Append('"').Append(token).Append('"'); i += token.Length - 1; }
        }
        return result.ToString();
    }
}
