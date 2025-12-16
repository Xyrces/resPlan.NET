using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ResPlan.Library;

namespace ResPlan.Demo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("ResPlan.NET Demo");

            // 1. JSON Sample (Quick Start)
            string jsonPath = "resplan_samples.json";
            if (File.Exists(jsonPath))
            {
                Console.WriteLine($"\n--- Processing JSON Samples ({jsonPath}) ---");
                ProcessPlans(PlanLoader.LoadPlans(jsonPath));
            }
            else
            {
                Console.WriteLine("resplan_samples.json not found.");
            }

            // 2. Real Dataset (Full)
            Console.WriteLine("\n--- Processing Real Dataset (ResPlan.pkl) ---");
            Console.WriteLine("Note: This requires Python with 'shapely' installed.");

            try
            {
                var manager = new DatasetManager();
                string pklPath = await manager.EnsureDatasetAvailable();
                Console.WriteLine($"Dataset available at: {pklPath}");

                Console.WriteLine("Loading Pickle (this may take time)...");

                var realPlans = PickleLoader.LoadFromPickle(pklPath);
                Console.WriteLine($"Loaded {realPlans.Count} plans from Pickle.");

                ProcessPlans(realPlans.Take(3).ToList(), "real");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Skipping real dataset processing: {ex.Message}");
            }
        }

        static void ProcessPlans(System.Collections.Generic.List<Plan> plans, string prefix = "csharp")
        {
            foreach (var plan in plans)
            {
                Console.WriteLine($"Processing Plan {plan.Id}...");

                // Generate Graph
                var graph = GraphGenerator.GenerateGraph(plan);
                Console.WriteLine($"  Graph Generated: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges.");

                // Render Image
                string outPath = $"{prefix}_plan_{plan.Id}.png";
                PlanRenderer.Render(plan, outPath);
                Console.WriteLine($"  Rendered to {outPath}");
            }
        }
    }
}
