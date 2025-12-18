using System;
using System.Collections.Generic;
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
