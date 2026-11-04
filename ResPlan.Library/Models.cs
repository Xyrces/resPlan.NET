using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MessagePack;
using NetTopologySuite.Geometries;
using ResPlan.Library.Data;

namespace ResPlan.Library
{
    public class ResPlanData
    {
        public int Id { get; set; }
        public Dictionary<string, List<string>> Geometries { get; set; } = new();
        public double[]? Bounds { get; set; }

        [JsonPropertyName("wall_width")]
        public double WallWidth { get; set; } = 0.1;
        public Dictionary<string, object?> Metadata { get; set; } = new();

        [JsonPropertyName("source_graph")]
        public GraphData? SourceGraph { get; set; }

        [JsonPropertyName("reference_graph")]
        public GraphData? ReferenceGraph { get; set; }
    }

    public class GraphData
    {
        public List<NodeData> Nodes { get; set; } = new();
        public List<EdgeData> Edges { get; set; } = new();
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }

    public class NodeData
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double? Area { get; set; }
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }

    public class EdgeData
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }

    [MessagePackObject]
    public class Plan
    {
        [Key(0)]
        public int Id { get; set; }
        [Key(1)]
        public Dictionary<string, List<Geometry>> Geometries { get; set; } = new Dictionary<string, List<Geometry>>();

        [JsonConverter(typeof(EnvelopeJsonConverter))]
        [Key(2)]
        public Envelope Bounds { get; set; } = new();

        [Key(3)]
        public Graph? ReferenceGraph { get; set; }

        [Key(4)]
        public double WallWidth { get; set; } = 0.1;

        [Key(5)]
        public Dictionary<string, object?> Metadata { get; set; } = new();

        [Key(6)]
        public Graph? SourceGraph { get; set; }

        public Coordinate? GetEntrance()
        {
            List<Geometry>? candidates = null;

            if (Geometries.TryGetValue("front_door", out var doors) && doors.Any())
            {
                candidates = doors;
            }
            else if (Geometries.TryGetValue("entrance", out var entrances) && entrances.Any())
            {
                 candidates = entrances;
            }

            if (candidates != null && candidates.Count > 0)
            {
                // Sort deterministically by Centroid X then Y
                var sorted = candidates.OrderBy(g => g.Centroid.X).ThenBy(g => g.Centroid.Y).ToList();
                return sorted[0].Centroid.Coordinate;
            }

            return null;
        }

        public List<Geometry> GetVerticalAnchors()
        {
            var anchors = new List<Geometry>();
            var keys = new[] { "stairs", "elevator", "foyer" };

            foreach (var key in keys)
            {
                if (Geometries.TryGetValue(key, out var geoms) && geoms != null)
                {
                    anchors.AddRange(geoms);
                }
            }

            if (anchors.Any())
            {
                return anchors;
            }

            if (Geometries.TryGetValue("corridor", out var corridors) && corridors != null && corridors.Any())
            {
                if (Bounds != null)
                {
                    var center = Bounds.Centre;
                    // Use the factory of the first corridor geometry to ensure compatibility
                    var factory = corridors.First().Factory;
                    var centerPoint = factory.CreatePoint(center);

                    var closest = corridors.OrderBy(g => g.Distance(centerPoint)).FirstOrDefault();
                    if (closest != null)
                    {
                        anchors.Add(closest);
                    }
                }
                else
                {
                    // If no bounds, just take the first corridor as fallback
                    anchors.Add(corridors.First());
                }
            }

            return anchors;
        }
    }

    [MessagePackObject]
    public class Graph
    {
        [Key(0)]
        public Dictionary<string, Node> Nodes { get; set; } = new Dictionary<string, Node>();
        [Key(1)]
        public List<Edge> Edges { get; set; } = new List<Edge>();
        [Key(2)]
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }

    [MessagePackObject]
    public class Node
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;
        [Key(1)]
        public string Type { get; set; } = string.Empty;
        [Key(2)]
        public Geometry? Geometry { get; set; }
        [Key(3)]
        public double Area { get; set; }
        [Key(4)]
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }

    [MessagePackObject]
    public class Edge
    {
        [Key(0)]
        public string SourceId { get; set; } = string.Empty;
        [Key(1)]
        public string TargetId { get; set; } = string.Empty;
        [Key(2)]
        public string Type { get; set; } = string.Empty;
        [Key(3)]
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }
}
