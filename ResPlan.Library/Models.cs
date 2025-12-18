using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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

    public class Plan
    {
        public int Id { get; set; }
        public Dictionary<string, List<Geometry>> Geometries { get; set; } = new Dictionary<string, List<Geometry>>();

        [JsonConverter(typeof(EnvelopeJsonConverter))]
        public Envelope Bounds { get; set; }

        public Graph ReferenceGraph { get; set; }
    }

    public class Graph
    {
        public Dictionary<string, Node> Nodes { get; set; } = new Dictionary<string, Node>();
        public List<Edge> Edges { get; set; } = new List<Edge>();
    }

    public class Node
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public Geometry Geometry { get; set; }
        public double Area { get; set; }
    }

    public class Edge
    {
        public string SourceId { get; set; }
        public string TargetId { get; set; }
        public string Type { get; set; }
    }
}
