using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ResPlan.Library;
using SkiaSharp;
using Xunit;

namespace ResPlan.Tests;

public class GeometryRegressionTests
{
    private static readonly GeometryFactory Factory = new();

    [Theory]
    [InlineData(0.1, false)]
    [InlineData(0.2, true)]
    [InlineData(0.0, false)]
    public void GenerateGraph_UsesPlanWallWidth(double wallWidth, bool adjacent)
    {
        var plan = new Plan { WallWidth = wallWidth };
        plan.Geometries["living"] = new() { Box(0, 0, 1, 1) };
        plan.Geometries["bedroom"] = new() { Box(1.3, 0, 2.3, 1) };

        var graph = GraphGenerator.GenerateGraph(plan);

        Assert.Equal(2, graph.Nodes.Count);
        if (adjacent)
        {
            var edge = Assert.Single(graph.Edges);
            Assert.Equal("bedroom_0", edge.SourceId);
            Assert.Equal("living_0", edge.TargetId);
            Assert.Equal("adjacency", edge.Type);
        }
        else
        {
            Assert.Empty(graph.Edges);
        }
    }

    [Fact]
    public void GenerateGraph_TraversesMultipartRoomsAndFrontDoors()
    {
        var room1 = Box(1, 1, 4, 4);
        var room2 = Box(6, 6, 9, 9);
        var plan = new Plan();
        plan.Geometries["bedroom"] = new()
        {
            Factory.CreateGeometryCollection(new Geometry[]
            {
                Factory.CreatePolygon(),
                Factory.CreateMultiPolygon(new[] { room1, room2 })
            })
        };
        plan.Geometries["front_door"] = new()
        {
            new WKTReader().Read("MULTILINESTRING ((1 1, 2 1), (6 6, 7 6))")
        };

        var graph = GraphGenerator.GenerateGraph(plan);

        Assert.Equal(4, graph.Nodes.Count);
        Assert.Same(room1, graph.Nodes["bedroom_0"].Geometry);
        Assert.Same(room2, graph.Nodes["bedroom_1"].Geometry);
        Assert.Equal(9, graph.Nodes["bedroom_0"].Area);
        Assert.IsType<LineString>(graph.Nodes["front_door_0"].Geometry);
        Assert.IsType<LineString>(graph.Nodes["front_door_1"].Geometry);
    }

    [Fact]
    public void GenerateGraph_DoesNotConnectRoomsThroughDisconnectedDoorParts()
    {
        var plan = new Plan();
        plan.Geometries["bathroom"] = new() { Box(0, 0, 1, 1) };
        plan.Geometries["living"] = new() { Box(3, 0, 4, 1) };
        plan.Geometries["door"] = new()
        {
            new WKTReader().Read("MULTILINESTRING ((0.5 0.5, 0.8 0.5), (3.5 0.5, 3.8 0.5))")
        };

        var graph = GraphGenerator.GenerateGraph(plan);

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Empty(graph.Edges);
    }

    [Theory]
    [InlineData(800, 400, 400, 80, 100, 200)]
    [InlineData(400, 800, 200, 280, 200, 100)]
    public void Render_NonSquareCanvasFitsAndCentersTheWholePlan(
        int width, int height, int roomX, int roomY, int marginX, int marginY)
    {
        var plan = new Plan { Bounds = new Envelope(0, 10, 0, 10) };
        plan.Geometries["living"] = new() { Box(0, 0, 10, 10) };
        plan.Geometries["bedroom"] = new() { Box(1, 7, 9, 9) };

        WithRenderedImage(plan, width, height, image =>
        {
            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);
            Assert.Equal(SKColor.Parse("#66c2a5"), image.GetPixel(roomX, roomY));
            Assert.Equal(SKColors.White, image.GetPixel(marginX, marginY));
        });
    }

    [Fact]
    public void Render_TraversesMultipartGeometryAndPreservesPolygonHoles()
    {
        var plan = new Plan { Bounds = new Envelope(0, 10, 0, 10) };
        plan.Geometries["bedroom"] = new()
        {
            new WKTReader().Read(
                "GEOMETRYCOLLECTION (POLYGON EMPTY, MULTIPOLYGON " +
                "(((1 1, 4 1, 4 4, 1 4, 1 1), (2 2, 3 2, 3 3, 2 3, 2 2)), " +
                "((6 6, 9 6, 9 9, 6 9, 6 6))))")
        };
        plan.Geometries["door"] = new()
        {
            new WKTReader().Read("MULTILINESTRING ((1 5, 3 5), (7 5, 9 5))")
        };

        WithRenderedImage(plan, 100, 100, image =>
        {
            var bedroomColor = SKColor.Parse("#66c2a5");
            Assert.Equal(bedroomColor, image.GetPixel(15, 85));
            Assert.Equal(bedroomColor, image.GetPixel(75, 25));
            Assert.Equal(SKColors.White, image.GetPixel(25, 75));
            Assert.Equal(SKColors.White, image.GetPixel(50, 50));
            Assert.NotEqual(SKColors.White, image.GetPixel(20, 50));
            Assert.NotEqual(SKColors.White, image.GetPixel(80, 50));
        });
    }

    private static Polygon Box(double minX, double minY, double maxX, double maxY) =>
        (Polygon)Factory.ToGeometry(new Envelope(minX, maxX, minY, maxY));

    private static void WithRenderedImage(Plan plan, int width, int height, Action<SKBitmap> verify)
    {
        var path = Path.Combine(Path.GetTempPath(), $"resplan-render-{Guid.NewGuid():N}.png");
        try
        {
            PlanRenderer.Render(plan, path, width, height);
            using var image = SKBitmap.Decode(path);
            Assert.NotNull(image);
            verify(image);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
