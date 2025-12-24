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
                Console.WriteLine("Loading Floor Plans...");

                var plans = await PlanLoader.LoadPlansAsync(maxItems: 5);
                if (plans.Count == 0)
                {
                    Console.WriteLine("No plans loaded.");
                    return;
                }

                Console.WriteLine($"Loaded {plans.Count} plans.");

                string outputDir = "output";
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                foreach (var plan in plans)
                {
                    string filename = Path.Combine(outputDir, $"plan_{plan.Id}.png");
                    PlanRenderer.Render(plan, filename);
                    Console.WriteLine($"Rendered Plan {plan.Id} to {filename}");

                    var entrance = plan.GetEntrance();
                    if (entrance != null)
                    {
                        Console.WriteLine($"  Plan {plan.Id} Entrance: {entrance}");
                    }
                    else
                    {
                        Console.WriteLine($"  Plan {plan.Id} has no entrance.");
                    }
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
