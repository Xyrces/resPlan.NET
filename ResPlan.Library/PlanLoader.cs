using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ResPlan.Library.Data;
using ResPlan.Library.PythonInfrastructure;

namespace ResPlan.Library
{
    public class PlanLoader
    {
        private static readonly WKTReader _wktReader = new WKTReader();

        public static async Task<List<Plan>> LoadPlansAsync(string jsonPath = null, string pklPathOverride = null, int? maxItems = null, Action<string> logger = null)
        {
            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                return LoadPlansFromJson(jsonPath);
            }

            // Python Loading Path (Subprocess approach due to Python.NET threading issues in some envs)

            Action<string> actualLogger = logger ?? Console.WriteLine;

            PythonEnvManager.EnsureDependencies(actualLogger);

            string pklPath;
            if (!string.IsNullOrEmpty(pklPathOverride))
            {
                 pklPath = pklPathOverride;
            }
            else
            {
                 await DataManager.EnsureDataAsync(actualLogger);
                 pklPath = DataManager.GetDataPath();
            }
            var plans = new List<Plan>();

            // We will run a python script that outputs JSON to stdout
            // This reuses resplan_loader.py logic but prints JSON instead of returning objects
            // The wrapper script is distributed with the library
            var wrapperScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resplan_loader_wrapper.py");
            if (!File.Exists(wrapperScript))
            {
                throw new FileNotFoundException("resplan_loader_wrapper.py not found");
            }

            actualLogger($"Loading data from {pklPath} using Python subprocess...");

            var pythonExe = PythonEnvManager.GetPythonPath(PythonEnvManager.GetVenvPath());

            var args = $"\"{wrapperScript}\" \"{pklPath}\"";
            if (maxItems.HasValue)
            {
                args += $" {maxItems.Value}";
            }

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            // Set environment to use the venv?
            // Invoking the python binary in venv/bin/python3 sets up sys.path correctly automatically.

            using (var p = Process.Start(psi))
            {
                var stdout = await p.StandardOutput.ReadToEndAsync();
                var stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (p.ExitCode != 0)
                {
                    throw new Exception($"Python loader failed: {stderr}");
                }

                // Parse stdout as JSON
                // The script should output JSON.
                // We might need to filter stdout if there are prints.
                // The wrapper script should avoid prints.

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                };

                // Find JSON start/end if there is noise
                var jsonStart = stdout.IndexOf('[');
                var jsonEnd = stdout.LastIndexOf(']');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonContent = stdout.Substring(jsonStart, jsonEnd - jsonStart + 1);
                     var dataList = JsonSerializer.Deserialize<List<ResPlanData>>(jsonContent, options);

                     foreach (var data in dataList)
                    {
                        var plan = ConvertDataToPlan(data);
                        plans.Add(plan);
                    }
                }
                else
                {
                     actualLogger("Warning: No JSON found in output.");
                     actualLogger(stdout);
                }
            }

            return plans;
        }

        private static Plan ConvertDataToPlan(ResPlanData data)
        {
            var plan = new Plan
            {
                Id = data.Id,
                Geometries = new Dictionary<string, List<Geometry>>()
            };

            if (data.Bounds != null && data.Bounds.Length == 4)
            {
                // Check for NaN or Infinity
                bool hasInvalidBounds = false;
                foreach (var val in data.Bounds)
                {
                    if (double.IsNaN(val) || double.IsInfinity(val))
                    {
                        hasInvalidBounds = true;
                        break;
                    }
                }

                if (hasInvalidBounds)
                {
                     plan.Bounds = new Envelope();
                }
                else
                {
                    plan.Bounds = new Envelope(data.Bounds[0], data.Bounds[2], data.Bounds[1], data.Bounds[3]);
                }
            }

            if (data.Geometries != null)
            {
                foreach (var kvp in data.Geometries)
                {
                    var geomList = new List<Geometry>();
                    if (kvp.Value != null)
                    {
                        foreach (var wkt in kvp.Value)
                        {
                            var geom = _wktReader.Read(wkt);
                            if (geom != null)
                            {
                                geomList.Add(geom);
                            }
                        }
                    }
                    plan.Geometries[kvp.Key] = geomList;
                }
            }

            if (data.ReferenceGraph != null)
            {
                plan.ReferenceGraph = new Graph();
                if (data.ReferenceGraph.Nodes != null)
                {
                    foreach(var n in data.ReferenceGraph.Nodes)
                    {
                        var area = n.Area ?? 0;
                        if (double.IsNaN(area) || double.IsInfinity(area))
                        {
                            area = 0;
                        }

                        plan.ReferenceGraph.Nodes[n.Id] = new Node
                        {
                            Id = n.Id,
                            Type = n.Type,
                            Area = area
                        };
                    }
                }
                if (data.ReferenceGraph.Edges != null)
                {
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
            }

            return plan;
        }

        // Keep synchronous legacy method for now if needed
        public static List<Plan> LoadPlans(string jsonPath)
        {
             return LoadPlansFromJson(jsonPath);
        }

        private static List<Plan> LoadPlansFromJson(string jsonPath)
        {
             // Reuse existing logic, wrapped in ConvertDataToPlan
            var json = File.ReadAllText(jsonPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            var dataList = JsonSerializer.Deserialize<List<ResPlanData>>(json, options);
            var plans = new List<Plan>();
            foreach (var data in dataList)
            {
                plans.Add(ConvertDataToPlan(data));
            }
            return plans;
        }
    }
}
