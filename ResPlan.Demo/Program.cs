using System;
using System.IO;
using System.Linq;
using ResPlan.Library;

namespace ResPlan.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("ResPlan.NET Demo");

            string jsonPath = "resplan_samples.json";
            if (!File.Exists(jsonPath))
            {
                Console.WriteLine($"Error: {jsonPath} not found. Run python script first.");
                return;
            }

            Console.WriteLine("Loading plans...");
            var plans = PlanLoader.LoadPlans(jsonPath);
            Console.WriteLine($"Loaded {plans.Count} plans.");

            foreach (var plan in plans)
            {
                Console.WriteLine($"Processing Plan {plan.Id}...");

                // 1. Generate Graph
                var graph = GraphGenerator.GenerateGraph(plan);
                Console.WriteLine($"  Graph Generated: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges.");

                // Verify against reference
                if (plan.ReferenceGraph != null)
                {
                    int refNodes = plan.ReferenceGraph.Nodes.Count;
                    int refEdges = plan.ReferenceGraph.Edges.Count;

                    Console.WriteLine($"  Reference Graph: {refNodes} nodes, {refEdges} edges.");

                    if (graph.Nodes.Count == refNodes && graph.Edges.Count == refEdges)
                    {
                        Console.WriteLine("  Graph Validation: MATCH");
                    }
                    else
                    {
                         Console.WriteLine("  Graph Validation: MISMATCH");
                    }
                }

                // 2. Render Image
                string outPath = $"csharp_plan_{plan.Id}.png";
                PlanRenderer.Render(plan, outPath);
                Console.WriteLine($"  Rendered to {outPath}");
            }
        }
    }
}
