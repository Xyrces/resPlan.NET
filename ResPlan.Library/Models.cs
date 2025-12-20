using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MessagePack;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
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

        public void Rotate(double angleRadians, Coordinate center)
        {
            var transform = new AffineTransformation();
            transform.Rotate(angleRadians, center.X, center.Y);

            ApplyTransformation(transform);
        }

        public void Translate(double dx, double dy)
        {
            var transform = new AffineTransformation();
            transform.Translate(dx, dy);

            ApplyTransformation(transform);
        }

        private void ApplyTransformation(AffineTransformation transform)
        {
            // Transform geometries
            foreach (var key in Geometries.Keys.ToList())
            {
                var originalList = Geometries[key];
                var newList = new List<Geometry>();
                foreach (var geom in originalList)
                {
                    var newGeom = transform.Transform(geom);
                    newList.Add(newGeom);
                }
                Geometries[key] = newList;
            }

            // Update Bounds
            var newEnvelope = new Envelope();
            foreach (var list in Geometries.Values)
            {
                foreach (var geom in list)
                {
                    newEnvelope.ExpandToInclude(geom.EnvelopeInternal);
                }
            }
            Bounds = newEnvelope;

            // Transform Graph Nodes
            if (ReferenceGraph != null && ReferenceGraph.Nodes != null)
            {
                foreach (var node in ReferenceGraph.Nodes.Values)
                {
                    if (node.Geometry != null)
                    {
                        node.Geometry = transform.Transform(node.Geometry);
                    }
                }
            }
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

    /// <summary>
    /// Represents a multi-story building composed of stacked floor plans.
    /// </summary>
    [MessagePackObject]
    public class Building
    {
        /// <summary>
        /// The list of floors in the building, typically ordered by floor number.
        /// </summary>
        [Key(0)]
        public List<BuildingFloor> Floors { get; set; } = new List<BuildingFloor>();
    }

    /// <summary>
    /// Represents a single floor within a building.
    /// </summary>
    [MessagePackObject]
    public class BuildingFloor
    {
        /// <summary>
        /// The 0-based index of the floor.
        /// </summary>
        [Key(0)]
        public int FloorNumber { get; set; }

        /// <summary>
        /// The floor plan associated with this level.
        /// </summary>
        [Key(1)]
        public Plan Plan { get; set; }

        /// <summary>
        /// Additional geometries generated for this floor (e.g., stairs, corridors) that are not part of the original plan.
        /// </summary>
        [Key(2)]
        public Dictionary<string, List<Geometry>> AdditionalGeometries { get; set; } = new Dictionary<string, List<Geometry>>();
    }
}
