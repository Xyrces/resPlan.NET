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
        public Dictionary<string, List<string>> Geometries { get; set; }
        public double[] Bounds { get; set; }

        [JsonPropertyName("reference_graph")]
        public GraphData ReferenceGraph { get; set; }
    }

    public class GraphData
    {
        public List<NodeData> Nodes { get; set; }
        public List<EdgeData> Edges { get; set; }
    }

    public class NodeData
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public double? Area { get; set; }
    }

    public class EdgeData
    {
        public string Source { get; set; }
        public string Target { get; set; }
        public string Type { get; set; }
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
        public Envelope Bounds { get; set; }

        [Key(3)]
        public Graph ReferenceGraph { get; set; }

        public Coordinate GetEntrance()
        {
            List<Geometry> candidates = null;

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
    }

    [MessagePackObject]
    public class Node
    {
        [Key(0)]
        public string Id { get; set; }
        [Key(1)]
        public string Type { get; set; }
        [Key(2)]
        public Geometry Geometry { get; set; }
        [Key(3)]
        public double Area { get; set; }
    }

    [MessagePackObject]
    public class Edge
    {
        [Key(0)]
        public string SourceId { get; set; }
        [Key(1)]
        public string TargetId { get; set; }
        [Key(2)]
        public string Type { get; set; }
    }
}
