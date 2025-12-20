using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using ResPlan.Library;
using Xunit;

namespace ResPlan.Tests
{
    public class BuildingGeneratorTests
    {
        [Fact]
        public async Task TestGenerateBuilding()
        {
            // Load plans (using truncated sample data usually, but we need enough data to stack)
            // We can rely on PlanLoader loading the sample dataset (ResPlan/ResPlan.pkl is downloaded if missing)
            // Or we can mock some plans.
            // Let's try to load real plans to ensure robustness.

            // Note: In test environment we might not have the full dataset.
            // But PlanLoader handles downloading.
            // We should limit items to avoid long wait.

            var plans = await PlanLoader.LoadPlansAsync(maxItems: 50);

            Assert.NotEmpty(plans);

            var generator = new BuildingGenerator();
            var building = generator.GenerateBuilding(plans, targetFloors: 5);

            Assert.NotNull(building);
            Assert.NotEmpty(building.Floors);

            // We expect at least 1 floor if plans exist
            Assert.True(building.Floors.Count >= 1);

            // Check Stair Core
            foreach(var floor in building.Floors)
            {
                Assert.True(floor.AdditionalGeometries.ContainsKey("stairs"));
                Assert.NotEmpty(floor.AdditionalGeometries["stairs"]);

                // Verify Front Door is at approx 0,0
                if (floor.Plan.Geometries.ContainsKey("front_door"))
                {
                    var fd = floor.Plan.Geometries["front_door"].FirstOrDefault();
                    if (fd != null)
                    {
                        var c = fd.Centroid.Coordinate;
                        Assert.InRange(c.X, -0.1, 0.1);
                        Assert.InRange(c.Y, -0.1, 0.1);
                    }
                }
            }

            // Check containment if >1 floor
            if (building.Floors.Count > 1)
            {
                for(int i=0; i<building.Floors.Count - 1; i++)
                {
                    var lower = building.Floors[i].Plan;
                    var upper = building.Floors[i+1].Plan;

                    // The bounds might not be strictly contained due to rotation "approx fit" logic
                    // But we can check that upper area <= lower area (due to sorting preference)
                    // The generator sorts descending, but also tries to find *best fit*.
                    // However, we start with largest.

                    // Actually, let's just assert validity of the building object.
                    Assert.True(upper.Bounds.Area <= lower.Bounds.Area * 1.5); // Upper shouldn't be massively bigger
                }
            }
        }
    }
}
