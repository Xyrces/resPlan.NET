using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using ResPlan.Library;
using Xunit;

namespace ResPlan.Tests
{
    public class AnchorTests
    {
        private GeometryFactory _factory = new GeometryFactory();

        private Polygon CreateBox(double x, double y, double size)
        {
            return _factory.CreatePolygon(new[]
            {
                new Coordinate(x, y),
                new Coordinate(x + size, y),
                new Coordinate(x + size, y + size),
                new Coordinate(x, y + size),
                new Coordinate(x, y)
            });
        }

        [Fact]
        public void GetVerticalAnchors_ReturnsStairs_WhenPresent()
        {
            var plan = new Plan();
            var stairs = CreateBox(0, 0, 10);
            plan.Geometries["stairs"] = new List<Geometry> { stairs };

            var anchors = plan.GetVerticalAnchors();

            Assert.Single(anchors);
            Assert.Equal(stairs, anchors[0]);
        }

        [Fact]
        public void GetVerticalAnchors_ReturnsElevator_WhenPresent()
        {
            var plan = new Plan();
            var elevator = CreateBox(10, 10, 5);
            plan.Geometries["elevator"] = new List<Geometry> { elevator };

            var anchors = plan.GetVerticalAnchors();

            Assert.Single(anchors);
            Assert.Equal(elevator, anchors[0]);
        }

        [Fact]
        public void GetVerticalAnchors_ReturnsFoyer_WhenPresent()
        {
            var plan = new Plan();
            var foyer = CreateBox(20, 20, 15);
            plan.Geometries["foyer"] = new List<Geometry> { foyer };

            var anchors = plan.GetVerticalAnchors();

            Assert.Single(anchors);
            Assert.Equal(foyer, anchors[0]);
        }

        [Fact]
        public void GetVerticalAnchors_ReturnsAllPriorityAnchors()
        {
            var plan = new Plan();
            var stairs = CreateBox(0, 0, 10);
            var elevator = CreateBox(20, 0, 5);
            plan.Geometries["stairs"] = new List<Geometry> { stairs };
            plan.Geometries["elevator"] = new List<Geometry> { elevator };

            var anchors = plan.GetVerticalAnchors();

            Assert.Equal(2, anchors.Count);
            Assert.Contains(stairs, anchors);
            Assert.Contains(elevator, anchors);
        }

        [Fact]
        public void GetVerticalAnchors_ReturnsCentralCorridor_WhenNoPriorityAnchors()
        {
            var plan = new Plan();
            // Bounds covering 0,0 to 100,100. Center is 50,50.
            plan.Bounds = new Envelope(0, 100, 0, 100);

            var corridorFar = CreateBox(0, 0, 10); // Center at 5,5. Dist to 50,50 ~ 63.6
            var corridorNear = CreateBox(45, 45, 10); // Center at 50,50. Dist to 50,50 = 0

            plan.Geometries["corridor"] = new List<Geometry> { corridorFar, corridorNear };

            var anchors = plan.GetVerticalAnchors();

            Assert.Single(anchors);
            Assert.Equal(corridorNear, anchors[0]);
        }

        [Fact]
        public void GetVerticalAnchors_ReturnsEmpty_WhenNoAnchorsOrCorridors()
        {
            var plan = new Plan();
            plan.Geometries["bedroom"] = new List<Geometry> { CreateBox(0, 0, 10) };

            var anchors = plan.GetVerticalAnchors();

            Assert.Empty(anchors);
        }

        [Fact]
        public void GetVerticalAnchors_FallbackCorridor_WhenBoundsNull()
        {
            var plan = new Plan();
            // Bounds is null
            var corridor1 = CreateBox(0, 0, 10);
            plan.Geometries["corridor"] = new List<Geometry> { corridor1 };

            var anchors = plan.GetVerticalAnchors();

            Assert.Single(anchors);
            Assert.Equal(corridor1, anchors[0]);
        }
    }
}
