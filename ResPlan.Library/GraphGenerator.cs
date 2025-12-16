using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace ResPlan.Library
{
    public class GraphGenerator
    {
        private static readonly List<string> RoomTypes = new List<string> { "living", "kitchen", "bedroom", "bathroom", "balcony" };
        private static readonly double WallWidth = 0.1;
        private static readonly double BufferFactor = 0.75;

        public static Graph GenerateGraph(Plan plan)
        {
            var G = new Graph();
            double buf = Math.Max(WallWidth * BufferFactor, 0.01);

            var nodesByType = new Dictionary<string, List<string>>();
            foreach (var type in RoomTypes) nodesByType[type] = new List<string>();
            nodesByType["front_door"] = new List<string>();

            // 1. Create nodes
            foreach (var roomType in RoomTypes)
            {
                if (plan.Geometries.ContainsKey(roomType))
                {
                    var geoms = plan.Geometries[roomType];
                    for (int i = 0; i < geoms.Count; i++)
                    {
                        var geom = geoms[i];
                        if (geom is Polygon && !geom.IsEmpty)
                        {
                            var nid = $"{roomType}_{i}";
                            var node = new Node
                            {
                                Id = nid,
                                Type = roomType,
                                Geometry = geom,
                                Area = geom.Area
                            };
                            G.Nodes[nid] = node;
                            nodesByType[roomType].Add(nid);
                        }
                    }
                }
            }

            if (plan.Geometries.ContainsKey("front_door"))
            {
                var geoms = plan.Geometries["front_door"];
                for (int i = 0; i < geoms.Count; i++)
                {
                    var geom = geoms[i];
                    var nid = $"front_door_{i}";
                    var node = new Node
                    {
                        Id = nid,
                        Type = "front_door",
                        Geometry = geom,
                        Area = geom.Area
                    };
                    G.Nodes[nid] = node;
                    nodesByType["front_door"].Add(nid);
                }
            }

            // Helper to get geometries
            List<Geometry> doors = plan.Geometries.ContainsKey("door") ? plan.Geometries["door"] : new List<Geometry>();
            List<Geometry> windows = plan.Geometries.ContainsKey("window") ? plan.Geometries["window"] : new List<Geometry>();

            var conns = new List<(Geometry Geom, string Type)>();
            doors.ForEach(d => conns.Add((d, "via_door")));
            windows.ForEach(w => conns.Add((w, "via_window")));

            // 2. Edges

            // front_door -> living (direct)
            foreach (var fd in nodesByType["front_door"])
            {
                var fdGeom = G.Nodes[fd].Geometry;
                foreach (var gen in nodesByType["living"])
                {
                    var genGeom = G.Nodes[gen].Geometry;
                    if (fdGeom.Intersects(genGeom.Buffer(buf)))
                    {
                        AddEdge(G, fd, gen, "direct");
                    }
                }
            }

            // adjacency: kitchen/bedroom <-> living
            var adjTypes = new[] { "kitchen", "bedroom" };
            foreach (var roomType in adjTypes)
            {
                foreach (var rn in nodesByType[roomType])
                {
                    var rGeom = G.Nodes[rn].Geometry.Buffer(buf);
                    foreach (var gen in nodesByType["living"])
                    {
                        var genGeom = G.Nodes[gen].Geometry;
                        var rGeomBuffered = G.Nodes[rn].Geometry.Buffer(buf).Buffer(buf);
                        var genGeomBuffered = G.Nodes[gen].Geometry.Buffer(buf);

                        if (rGeomBuffered.Intersects(genGeomBuffered))
                        {
                            AddEdge(G, rn, gen, "adjacency");
                        }
                    }
                }
            }

            // bathroom & balcony connections via door/window to living/bedroom
            var connTypes = new[] { "bathroom", "balcony" };
            foreach (var roomType in connTypes)
            {
                foreach (var rn in nodesByType[roomType])
                {
                    var rGeom = G.Nodes[rn].Geometry.Buffer(buf);
                    foreach (var conn in conns)
                    {
                        if (!conn.Geom.Intersects(rGeom)) continue;

                        foreach (var targetType in new[] { "living", "bedroom" })
                        {
                            foreach (var tn in nodesByType[targetType])
                            {
                                var tGeom = G.Nodes[tn].Geometry.Buffer(buf);
                                if (conn.Geom.Intersects(tGeom))
                                {
                                    AddEdge(G, rn, tn, conn.Type);
                                }
                            }
                        }
                    }
                }
            }

            return G;
        }

        private static void AddEdge(Graph G, string u, string v, string type)
        {
            // Check if edge exists (undirected)
            bool exists = G.Edges.Any(e =>
                (e.SourceId == u && e.TargetId == v) ||
                (e.SourceId == v && e.TargetId == u));

            if (!exists)
            {
                G.Edges.Add(new Edge { SourceId = u, TargetId = v, Type = type });
            }
        }
    }
}
