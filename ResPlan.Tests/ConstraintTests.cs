using System.Collections.Generic;
using NetTopologySuite.Geometries;
using ResPlan.Library;
using Xunit;

namespace ResPlan.Tests
{
    public class ConstraintTests
    {
        [Fact]
        public void IsPlanCompatible_RotatedPlanInRotatedLot_ReturnsTrue()
        {
            // Arrange
            var geometryFactory = new GeometryFactory();

            // Create a Plan with a 10x10 square at (0,0)
            // Centered at 5,5
            var coords = new Coordinate[]
            {
                new Coordinate(0, 0),
                new Coordinate(10, 0),
                new Coordinate(10, 10),
                new Coordinate(0, 10),
                new Coordinate(0, 0)
            };
            var shell = geometryFactory.CreateLinearRing(coords);
            var poly = geometryFactory.CreatePolygon(shell);

            var plan = new Plan
            {
                Id = 1,
                Geometries = new Dictionary<string, List<Geometry>>
                {
                    { "room", new List<Geometry> { poly } }
                }
            };
            // Initial bounds (0,0) to (10,10)
            plan.Bounds = new Envelope(0, 10, 0, 10);

            // Rotate the plan by 45 degrees around its center (5,5)
            plan.Rotate(System.Math.PI / 4.0, new Coordinate(5, 5));

            // Now create a bounding polygon that matches the rotated plan geometry exactly
            // We can cheat by using the plan's geometry itself (the rotated poly)
            // But to be rigorous, let's use the rotated poly from the plan
            var rotatedGeom = plan.Geometries["room"][0];
            Assert.True(rotatedGeom is Polygon);
            var boundingPolygon = (Polygon)rotatedGeom.Copy();

            // The bounding polygon now matches the plan's geometry.
            // However, plan.Bounds (AABB) will be larger than the rotated geometry.
            // AABB of a 45-degree rotated 10x10 square is roughly 14.14x14.14.
            // This AABB will NOT be contained in the 10x10 rotated square (which is the boundingPolygon).

            // Act
            bool isCompatible = PlanLoader.IsPlanCompatible(plan, boundingPolygon);

            // Assert
            // This should be true if we are checking geometry containment correctly.
            // It will be false if we are only checking AABB containment.
            Assert.True(isCompatible, "Plan should be compatible with a bounding polygon that exactly matches its geometry.");
        }
    }
}
