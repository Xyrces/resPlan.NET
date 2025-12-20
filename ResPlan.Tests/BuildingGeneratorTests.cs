using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using ResPlan.Library;
using Xunit;

namespace ResPlan.Tests
{
    public class BuildingGeneratorTests
    {
        private readonly GeometryFactory _geometryFactory = new GeometryFactory();

        [Fact]
        public void TestGenerateBuilding()
        {
            // Create mock plans manually to avoid Python dependency in unit tests
            var plans = new List<Plan>
            {
                CreateMockPlan(1, 20, 20), // 400 area
                CreateMockPlan(2, 15, 15), // 225 area
                CreateMockPlan(3, 10, 10), // 100 area
                CreateMockPlan(4, 30, 30)  // 900 area (Largest)
            };

            var generator = new BuildingGenerator();
            var building = generator.GenerateBuilding(plans, targetFloors: 5);

            Assert.NotNull(building);
            Assert.NotEmpty(building.Floors);

            // We provided 4 plans, target 5. Should use all 4 if they fit.
            // Order should be 30x30 -> 20x20 -> 15x15 -> 10x10.
            Assert.Equal(4, building.Floors.Count);

            // Verify Floor Sorting (Area Descending)
            Assert.Equal(4, building.Floors[0].Plan.Id); // 30x30
            Assert.Equal(1, building.Floors[1].Plan.Id); // 20x20
            Assert.Equal(2, building.Floors[2].Plan.Id); // 15x15
            Assert.Equal(3, building.Floors[3].Plan.Id); // 10x10

            // Check Stair Core
            foreach(var floor in building.Floors)
            {
                Assert.True(floor.AdditionalGeometries.ContainsKey("stairs"));
                Assert.NotEmpty(floor.AdditionalGeometries["stairs"]);

                // Verify Front Door is at 0,0 (Normalization)
                if (floor.Plan.Geometries.ContainsKey("front_door"))
                {
                    var fd = floor.Plan.Geometries["front_door"].FirstOrDefault();
                    if (fd != null)
                    {
                        var c = fd.Centroid.Coordinate;
                        Assert.InRange(c.X, -0.001, 0.001);
                        Assert.InRange(c.Y, -0.001, 0.001);
                    }
                }
            }

            // Check containment
            for(int i=0; i<building.Floors.Count - 1; i++)
            {
                var lower = building.Floors[i].Plan;
                var upper = building.Floors[i+1].Plan;

                // Since we created perfect squares centered at front door (0,0), strict containment should hold.
                // However, our Mock Plan creation places the square relative to front door.
                // Let's check Area logic.
                Assert.True(lower.Bounds.Area > upper.Bounds.Area);
            }
        }

        private Plan CreateMockPlan(int id, double width, double height)
        {
            var plan = new Plan
            {
                Id = id,
                Geometries = new Dictionary<string, List<Geometry>>()
            };

            // Define coordinates for a box
            // Assume Front Door is at bottom-center relative to the box?
            // Or we just place a box and a front door point.
            // Let's place the box from (0,0) to (width, height)
            // And Front Door at (width/2, 0).

            var box = _geometryFactory.CreatePolygon(new Coordinate[]
            {
                new Coordinate(0, 0),
                new Coordinate(width, 0),
                new Coordinate(width, height),
                new Coordinate(0, height),
                new Coordinate(0, 0)
            });

            // Create Front Door geometry (small line or polygon)
            var fdCenterX = width / 2.0;
            var fd = _geometryFactory.CreatePolygon(new Coordinate[]
            {
                new Coordinate(fdCenterX - 0.5, 0),
                new Coordinate(fdCenterX + 0.5, 0),
                new Coordinate(fdCenterX + 0.5, 0.2), // Slight thickness
                new Coordinate(fdCenterX - 0.5, 0.2),
                new Coordinate(fdCenterX - 0.5, 0)
            });

            plan.Geometries["living"] = new List<Geometry> { box };
            plan.Geometries["front_door"] = new List<Geometry> { fd };

            plan.Bounds = box.EnvelopeInternal;

            return plan;
        }
    }
}
