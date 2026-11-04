using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace ResPlan.Library
{
    public class GraphGenerator
    {
        private static readonly List<string> RoomTypes = new List<string> { "living", "kitchen", "bedroom", "bathroom", "balcony" };
        private static readonly double BufferFactor = 0.75;
        // Shapely's geometry.buffer() uses 16 segments per quadrant; NTS defaults
        // to 8, which can omit intersections close to a rounded buffer boundary.
        private const int BufferQuadrantSegments = 16;

        public static Graph GenerateGraph(Plan plan)
        {
            var G = new Graph();
            // Python treats a missing or zero wall width as the default width.
            double wallWidth = plan.WallWidth == 0 ? 0.1 : plan.WallWidth;
            double buf = Math.Max(wallWidth * BufferFactor, 0.01);

            var nodesByType = new Dictionary<string, List<string>>();
            foreach (var type in RoomTypes) nodesByType[type] = new List<string>();
            nodesByType["front_door"] = new List<string>();

            // 1. Create nodes
            foreach (var roomType in RoomTypes)
            {
                if (plan.Geometries.ContainsKey(roomType))
                {
                    var geoms = GeometryParts.Enumerate(plan.Geometries[roomType]).ToList();
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
                var geoms = GeometryParts.Enumerate(plan.Geometries["front_door"]).ToList();
                for (int i = 0; i < geoms.Count; i++)
                {
                    var geom = geoms[i];
                    var nid = $"front_door_{i}";
                    // front_door might be Polygon or LineString
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
            var doors = plan.Geometries.TryGetValue("door", out var doorGeometries)
                ? GeometryParts.Enumerate(doorGeometries) : Enumerable.Empty<Geometry>();
            var windows = plan.Geometries.TryGetValue("window", out var windowGeometries)
                ? GeometryParts.Enumerate(windowGeometries) : Enumerable.Empty<Geometry>();

            var conns = new List<(Geometry Geom, string Type)>();
            conns.AddRange(doors.Select(d => (d, "via_door")));
            conns.AddRange(windows.Select(w => (w, "via_window")));

            // 2. Edges

            // front_door -> living (direct)
            foreach (var fd in nodesByType["front_door"])
            {
                var fdGeom = G.Nodes[fd].Geometry!;
                foreach (var gen in nodesByType["living"])
                {
                    var genGeom = G.Nodes[gen].Geometry!;
                    // Python: fd_geom.intersects(gen_geom.buffer(buf))
                    if (fdGeom.Intersects(genGeom.Buffer(buf, BufferQuadrantSegments)))
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
                    var rGeomBuffered = G.Nodes[rn].Geometry!.Buffer(buf, BufferQuadrantSegments)
                        .Buffer(buf, BufferQuadrantSegments);
                    foreach (var gen in nodesByType["living"])
                    {
                        // Preserve the reference's two room buffers and one living buffer.
                        var genGeomBuffered = G.Nodes[gen].Geometry!.Buffer(buf, BufferQuadrantSegments);

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
                    var rGeom = G.Nodes[rn].Geometry!.Buffer(buf, BufferQuadrantSegments);
                    foreach (var conn in conns)
                    {
                        if (!conn.Geom.Intersects(rGeom)) continue;

                        foreach (var targetType in new[] { "living", "bedroom" })
                        {
                            foreach (var tn in nodesByType[targetType])
                            {
                                var tGeom = G.Nodes[tn].Geometry!.Buffer(buf, BufferQuadrantSegments);
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

    internal static class GeometryParts
    {
        internal static IEnumerable<Geometry> Enumerate(IEnumerable<Geometry>? geometries)
        {
            if (geometries == null) yield break;

            foreach (var geometry in geometries)
            {
                if (geometry == null || geometry.IsEmpty) continue;

                if (geometry is GeometryCollection collection)
                {
                    foreach (var part in Enumerate(collection.Geometries))
                    {
                        yield return part;
                    }
                }
                else
                {
                    yield return geometry;
                }
            }
        }
    }
}
