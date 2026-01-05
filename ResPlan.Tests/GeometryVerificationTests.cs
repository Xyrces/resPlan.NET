using System.Linq;
using NetTopologySuite.Geometries;
using Xunit;
using ResPlan.Library;

namespace ResPlan.Tests
{
    public class GeometryVerificationTests
    {
        [Fact]
        public void VerifyAllGeometriesAreValidAndClosed()
        {
            string jsonPath = "resplan_samples.json";
            Assert.True(System.IO.File.Exists(jsonPath), $"Test data {jsonPath} not found.");

            var plans = PlanLoader.LoadPlans(jsonPath);
            Assert.NotEmpty(plans);

            foreach (var plan in plans)
            {
                foreach (var kvp in plan.Geometries)
                {
                    var layerName = kvp.Key;
                    var geometries = kvp.Value;

                    foreach (var geometry in geometries)
                    {
                        // 1. Check validity
                        Assert.True(geometry.IsValid, $"Invalid geometry found in plan {plan.Id}, layer {layerName}. Reason: {geometry.IsValid}");

                        // 2. Check for closed rings if it's a polygon or multipolygon
                        if (geometry is Polygon polygon)
                        {
                            ValidatePolygon(polygon, plan.Id, layerName);
                        }
                        else if (geometry is MultiPolygon multiPolygon)
                        {
                            foreach (var g in multiPolygon.Geometries)
                            {
                                if (g is Polygon p)
                                    ValidatePolygon(p, plan.Id, layerName);
                            }
                        }
                        
                        // 3. Ensure non-empty
                        Assert.False(geometry.IsEmpty, $"Empty geometry found in plan {plan.Id}, layer {layerName}");
                    }
                }
            }
        }

        private void ValidatePolygon(Polygon polygon, int planId, string layerName)
        {
            // shell
            Assert.True(polygon.ExteriorRing.IsClosed, $"Polygon exterior ring not closed in plan {planId}, layer {layerName}");
            // holes
            foreach (var hole in polygon.InteriorRings)
            {
                Assert.True(hole.IsClosed, $"Polygon interior ring not closed in plan {planId}, layer {layerName}");
            }
        }
    }
}
