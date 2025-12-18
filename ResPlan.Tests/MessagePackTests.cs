using System.Collections.Generic;
using System.IO;
using MessagePack;
using NetTopologySuite.Geometries;
using ResPlan.Library;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests
{
    public class MessagePackTests
    {
        [Fact]
        public void CanSerializeAndDeserializePlan()
        {
            // Arrange
            var plan = new Plan
            {
                Id = 1,
                Bounds = new Envelope(0, 10, 0, 10),
                ReferenceGraph = new Graph
                {
                    Nodes = new Dictionary<string, Node>
                    {
                        { "n1", new Node { Id = "n1", Type = "room", Area = 50.0, Geometry = new Point(5, 5) } }
                    },
                    Edges = new List<Edge>
                    {
                        new Edge { SourceId = "n1", TargetId = "n2", Type = "door" }
                    }
                },
                Geometries = new Dictionary<string, List<Geometry>>
                {
                    { "walls", new List<Geometry> { new LineString(new[] { new Coordinate(0,0), new Coordinate(10,0) }) } }
                }
            };

            // Act
            var bytes = PlanSerializer.Serialize(plan);
            var deserialized = PlanSerializer.Deserialize(bytes);

            // Assert
            Assert.Equal(plan.Id, deserialized.Id);
            Assert.NotNull(deserialized.Bounds);
            Assert.Equal(plan.Bounds.MinX, deserialized.Bounds.MinX);
            Assert.Equal(plan.ReferenceGraph.Nodes.Count, deserialized.ReferenceGraph.Nodes.Count);
            Assert.Equal(plan.ReferenceGraph.Nodes["n1"].Area, deserialized.ReferenceGraph.Nodes["n1"].Area);
            Assert.True(plan.ReferenceGraph.Nodes["n1"].Geometry.EqualsExact(deserialized.ReferenceGraph.Nodes["n1"].Geometry));
            Assert.Equal(plan.Geometries["walls"][0].ToText(), deserialized.Geometries["walls"][0].ToText());
        }

        [Fact]
        public void CanSerializePlanWithNaNArea()
        {
             // Arrange
            var plan = new Plan
            {
                Id = 2,
                Bounds = new Envelope(0, 10, 0, 10),
                ReferenceGraph = new Graph
                {
                    Nodes = new Dictionary<string, Node>
                    {
                        { "n1", new Node { Id = "n1", Area = double.NaN } },
                        { "n2", new Node { Id = "n2", Area = double.PositiveInfinity } }
                    }
                }
            };

            // Act
            var bytes = PlanSerializer.Serialize(plan);
            var deserialized = PlanSerializer.Deserialize(bytes);

            // Assert
            Assert.True(double.IsNaN(deserialized.ReferenceGraph.Nodes["n1"].Area));
            Assert.True(double.IsPositiveInfinity(deserialized.ReferenceGraph.Nodes["n2"].Area));
        }

        [Fact]
        public void CanSerializeNullGeometry()
        {
             // Arrange
            var plan = new Plan
            {
                Id = 3,
                ReferenceGraph = new Graph
                {
                    Nodes = new Dictionary<string, Node>
                    {
                        { "n1", new Node { Id = "n1", Geometry = null } }
                    }
                }
            };

            // Act
            var bytes = PlanSerializer.Serialize(plan);
            var deserialized = PlanSerializer.Deserialize(bytes);

            // Assert
            Assert.Null(deserialized.ReferenceGraph.Nodes["n1"].Geometry);
        }
    }
}
