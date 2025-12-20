using System;
using System.IO;
using System.Threading.Tasks;
using ResPlan.Library;

namespace ResPlan.Demo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("ResPlan Demo");
                Console.WriteLine("Generating Multi-Story Building...");

                var plans = await PlanLoader.LoadPlansAsync(maxItems: 50);
                if (plans.Count == 0)
                {
                    Console.WriteLine("No plans loaded.");
                    return;
                }

                var generator = new BuildingGenerator();
                var building = generator.GenerateBuilding(plans, 3);

                Console.WriteLine($"Generated Building with {building.Floors.Count} floors.");

                // Render each floor
                string outputDir = "output";
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                foreach (var floor in building.Floors)
                {
                    string filename = Path.Combine(outputDir, $"floor_{floor.FloorNumber}.png");
                    PlanRenderer.RenderFloor(floor, filename);
                    Console.WriteLine($"Rendered {filename}");
                }

                Console.WriteLine("Done.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
