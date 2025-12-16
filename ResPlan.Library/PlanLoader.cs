using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace ResPlan.Library
{
    public class PlanLoader
    {
        private static readonly WKTReader _wktReader = new WKTReader();

        public static List<Plan> LoadPlans(string jsonPath)
        {
            var json = File.ReadAllText(jsonPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var dataList = JsonSerializer.Deserialize<List<ResPlanData>>(json, options);
            var plans = new List<Plan>();

            foreach (var data in dataList)
            {
                var plan = new Plan
                {
                    Id = data.Id,
                    Geometries = new Dictionary<string, List<Geometry>>()
                };

                if (data.Bounds != null && data.Bounds.Length == 4)
                {
                    plan.Bounds = new Envelope(data.Bounds[0], data.Bounds[2], data.Bounds[1], data.Bounds[3]);
                }

                foreach (var kvp in data.Geometries)
                {
                    var geomList = new List<Geometry>();
                    foreach (var wkt in kvp.Value)
                    {
                        var geom = _wktReader.Read(wkt);
                        if (geom != null)
                        {
                            geomList.Add(geom);
                        }
                    }
                    plan.Geometries[kvp.Key] = geomList;
                }

                // Load Reference Graph
                if (data.ReferenceGraph != null)
                {
                    plan.ReferenceGraph = new Graph();
                    foreach(var n in data.ReferenceGraph.Nodes)
                    {
                        plan.ReferenceGraph.Nodes[n.Id] = new Node
                        {
                            Id = n.Id,
                            Type = n.Type,
                            Area = n.Area ?? 0
                        };
                    }
                    foreach(var e in data.ReferenceGraph.Edges)
                    {
                        plan.ReferenceGraph.Edges.Add(new Edge
                        {
                            SourceId = e.Source,
                            TargetId = e.Target,
                            Type = e.Type
                        });
                    }
                }

                plans.Add(plan);
            }

            return plans;
        }
    }
}
