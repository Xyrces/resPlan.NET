using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Pickle.Net;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests;

public class NativePickleLoaderTests
{
    [Fact]
    public void LoadsRealPythonSampleIncludingNumpyAndNetworkXState()
    {
        var plans = NativePickleLoader.Load(Path.Combine(AppContext.BaseDirectory, "sample.pkl"));
        Assert.Equal(2, plans.Count);
        Assert.Equal(0, plans[0].Id);
        Assert.Equal(1, plans[1].Id);
        Assert.Equal(13L, plans[0].Metadata["id"]);
        Assert.Equal(0L, plans[1].Metadata["id"]);
        Assert.Equal("Apartment", plans[0].Metadata["unitType"]);
        Assert.Equal(73.13270853717651, Assert.IsType<double>(plans[0].Metadata["net_area"]), 10);
        Assert.Contains("garden", plans[0].Geometries.Keys);
        Assert.Empty(plans[0].Geometries["garden"]);
        Assert.NotEmpty(plans[0].Geometries["neighbor"]);
        Assert.Same(plans[0].Geometries["stair"], plans[0].Geometries["stairs"]);
        var source = Assert.IsType<Library.Graph>(plans[0].SourceGraph);
        Assert.Equal(9, source.Nodes.Count);
        Assert.Equal(8, source.Edges.Count);
        Assert.NotNull(source.Nodes["living_0"].Geometry);
        Assert.NotEmpty(plans[0].ReferenceGraph!.Nodes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void PreservesGeometryCategoriesAndMetadataAcrossProtocols(int protocol)
    {
        var living = Shape("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
        var stairs = Shape("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))");
        var input = new Dictionary<string, object?>
        {
            ["id"] = 4096L,
            ["living"] = living,
            ["stair"] = stairs,
            ["entrance"] = Shape("POINT (0 1)"),
            ["balacony"] = Shape("POLYGON EMPTY"),
            ["garden"] = Shape("MULTIPOLYGON EMPTY"),
            ["neighbor"] = new PickleTuple([new PickleTuple([Shape("POINT (100 100)")]), Shape("POINT (200 200)")]),
            ["wall_width"] = 0.25,
            ["unitType"] = "Apartment",
            ["metadata"] = new Dictionary<string, object?> { ["dimensions"] = new PickleTuple([1L, 2.5, "east"]) }
        };
        var plan = LoadRecords(protocol, input).Single();
        Assert.Equal(0, plan.Id);
        Assert.Equal(4096L, plan.Metadata["id"]);
        Assert.Equal(0.25, plan.WallWidth);
        Assert.Equal(2, plan.Geometries["neighbor"].Count);
        Assert.Equal(new Envelope(0, 2, 0, 2), plan.Bounds); // Neighbors do not expand the viewport.
        Assert.Contains("balcony", plan.Geometries.Keys);
        Assert.DoesNotContain("balacony", plan.Geometries.Keys);
        Assert.Empty(plan.Geometries["garden"]);
        Assert.Single(plan.GetVerticalAnchors());
        Assert.Equal(new Coordinate(0, 1), plan.GetEntrance());
        var metadata = Assert.IsType<Dictionary<string, object?>>(plan.Metadata["metadata"]);
        Assert.Equal(new object?[] { 1L, 2.5, "east" }, Assert.IsType<object?[]>(metadata["dimensions"]));
    }

    [Fact]
    public void CanonicalBalconyTakesPrecedenceWithoutDiscardingOriginalTypoCategory()
    {
        var plan = LoadRecords(5, new Dictionary<string, object?>
        {
            ["balcony"] = Shape("POINT (1 1)"), ["balacony"] = Shape("POINT (2 2)")
        }).Single();
        Assert.Equal(1, plan.Geometries["balcony"].Single().Coordinate.X);
        Assert.Equal(2, plan.Geometries["balacony"].Single().Coordinate.X);
    }

    [Fact]
    public void PreservesNetworkXGraphNodeAndEdgeAttributesAndSharedGeometry()
    {
        var geometry = Shape("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
        var nodes = new Dictionary<string, object?>
        {
            ["a"] = new Dictionary<string, object?> { ["geometry"] = geometry, ["type"] = "living", ["area"] = 4.0, ["label"] = "North" },
            ["b"] = new Dictionary<string, object?> { ["type"] = "exit", ["area"] = 0.0 }
        };
        var edge = new Dictionary<string, object?> { ["type"] = "direct", ["weight"] = 2.5 };
        var state = new Dictionary<string, object?>
        {
            ["graph"] = new Dictionary<string, object?> { ["name"] = "Imported" },
            ["_node"] = nodes,
            ["_adj"] = new Dictionary<string, object?>
            {
                ["a"] = new Dictionary<string, object?> { ["b"] = edge },
                ["b"] = new Dictionary<string, object?> { ["a"] = edge }
            },
            ["nodes"] = new NodeViewValue(new Dictionary<string, object?> { ["_nodes"] = nodes })
        };
        var plan = LoadRecords(5, new Dictionary<string, object?> { ["living"] = geometry, ["graph"] = new GraphValue(state) }).Single();
        var graph = Assert.IsType<Library.Graph>(plan.SourceGraph);
        Assert.Equal("Imported", graph.Metadata["name"]);
        Assert.Equal("North", graph.Nodes["a"].Metadata["label"]);
        Assert.Same(plan.Geometries["living"][0], graph.Nodes["a"].Geometry);
        Assert.Equal(2.5, Assert.Single(graph.Edges).Metadata["weight"]);
        Assert.Single(plan.ReferenceGraph!.Nodes);
        Assert.NotSame(graph, plan.ReferenceGraph);
    }

    [Theory]
    [InlineData("i2", ">", "FFFE", -2.0)]
    [InlineData("i4", "<", "85FFFFFF", -123.0)]
    [InlineData("i8", ">", "FFFFFFFFFFFFFF85", -123.0)]
    [InlineData("u4", ">", "F0000000", 4026531840.0)]
    [InlineData("f2", ">", "3E00", 1.5)]
    [InlineData("f4", ">", "3FC00000", 1.5)]
    [InlineData("f8", "<", "000000000000F83F", 1.5)]
    [InlineData("f8", ">", "3FF8000000000000", 1.5)]
    public void DecodesNumpyScalarByteOrderAndWidth(string descriptor, string byteOrder, string hex, double expected)
    {
        var scalar = new ScalarValue(new DtypeValue(descriptor, byteOrder), Convert.FromHexString(hex));
        var plan = LoadRecords(5, new Dictionary<string, object?> { ["value"] = scalar }).Single();
        Assert.Equal(expected, Convert.ToDouble(plan.Metadata["value"]));
    }

    [Fact]
    public void PreservesUnsigned64BooleanUnicodeAndNonfiniteNumpyValues()
    {
        var plan = LoadRecords(5, new Dictionary<string, object?>
        {
            ["unsigned"] = new ScalarValue(new DtypeValue("u8", ">"), Convert.FromHexString("FFFFFFFFFFFFFFFF")),
            ["boolean"] = new ScalarValue(new DtypeValue("b1", "|"), [1]),
            ["unicode"] = new ScalarValue(new DtypeValue("U2", ">"), Convert.FromHexString("000000410001F600")),
            ["bytes"] = new ScalarValue(new DtypeValue("S3", "|"), [65, 0, 0]),
            ["nan"] = new ScalarValue(new DtypeValue("f8", ">"), Convert.FromHexString("7FF8000000000000"))
        }).Single();
        Assert.Equal(ulong.MaxValue, plan.Metadata["unsigned"]);
        Assert.Equal(true, plan.Metadata["boolean"]);
        Assert.Equal("A😀", plan.Metadata["unicode"]);
        Assert.Equal(new byte[] { 65 }, Assert.IsType<byte[]>(plan.Metadata["bytes"]));
        Assert.True(double.IsNaN(Assert.IsType<double>(plan.Metadata["nan"])));
    }

    [Fact]
    public void AcceptsTheNumpyOneModuleAlias()
    {
        var registry = WriterRegistry();
        registry.RegisterReducer<ScalarValue>((scalar, _) => new PickleReduction(new PickleGlobal("numpy.core.multiarray", "scalar"), new PickleTuple([scalar.Dtype, scalar.Bytes])));
        var bytes = PickleSerializer.Dumps(new[] { new Dictionary<string, object?> { ["value"] = new ScalarValue(new DtypeValue("i1", "|"), [254]) } }, options: new PicklerOptions { Registry = registry });
        Assert.Equal(-2L, NativePickleLoader.Load(new MemoryStream(bytes)).Single().Metadata["value"]);
    }

    [Fact]
    public void RejectsUnknownGlobalsBeforeInvokingAnyCode()
    {
        var payload = Encoding.ASCII.GetBytes("cos\nsystem\n(S'echo unsafe'\ntR.");
        var exception = Assert.Throws<InvalidDataException>(() => NativePickleLoader.Load(new MemoryStream(payload)));
        Assert.Contains("os.system", exception.Message);
        Assert.Contains("not registered", exception.Message);
    }

    [Theory]
    [InlineData("O8", 8)]
    [InlineData("f16", 16)]
    [InlineData("f8", 4)]
    public void RejectsUnsupportedDtypesAndTruncatedScalarBuffers(string descriptor, int length)
    {
        var exception = Assert.Throws<InvalidDataException>(() => LoadRecords(5,
            new Dictionary<string, object?> { ["value"] = new ScalarValue(new DtypeValue(descriptor, "<"), new byte[length]) }));
        Assert.Contains("NumPy", exception.Message);
    }

    [Fact]
    public void RejectsMalformedWkbAndNonPlanInput()
    {
        var registry = WriterRegistry();
        registry.RegisterReducer<WkbValue>((_, _) => new PickleReduction(new PickleGlobal("shapely.io", "from_wkb"), new PickleTuple([new byte[] { 1, 2, 3 }])));
        var bytes = PickleSerializer.Dumps(new[] { new Dictionary<string, object?> { ["living"] = Shape("POINT (0 0)") } }, options: new PicklerOptions { Registry = registry });
        Assert.Throws<InvalidDataException>(() => NativePickleLoader.Load(new MemoryStream(bytes)));
        Assert.Throws<InvalidDataException>(() => NativePickleLoader.Load(new MemoryStream(PickleSerializer.Dumps("not a dataset"))));
    }

    [Fact]
    public void RejectsUnsupportedMetadataInsteadOfDroppingIt()
    {
        var exception = Assert.Throws<InvalidDataException>(() => LoadRecords(5,
            new Dictionary<string, object?> { ["metadata"] = new Dictionary<string, object?> { ["shape"] = Shape("POINT (0 0)") } }));
        Assert.Contains("item 0", exception.Message);
        Assert.Contains("not discarded", exception.Message);
    }

    [Fact]
    public void AppliesLimitsAndCancellationAndLeavesCallerStreamOpen()
    {
        using var stream = new MemoryStream(PickleSerializer.Dumps(new[] { new Dictionary<string, object?>(), new Dictionary<string, object?>() }));
        Assert.Single(NativePickleLoader.Load(stream, 1));
        Assert.True(stream.CanRead);
        stream.Position = 0;
        Assert.Empty(NativePickleLoader.Load(stream, 0));
        Assert.Equal(0, stream.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => NativePickleLoader.Load(stream, -1));
        Assert.Throws<OperationCanceledException>(() => NativePickleLoader.Load(stream, cancellationToken: new CancellationToken(true)));
        Assert.Throws<InvalidDataException>(() => NativePickleLoader.Load(stream, maxInputBytes: 1));
        Assert.Throws<InvalidDataException>(() => NativePickleLoader.Load(stream, maxMemoEntries: 1));
    }

    [Fact]
    public void ObservesCancellationDuringDecoding()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancelAfterReadStream(PickleSerializer.Dumps(new[] { new Dictionary<string, object?>() }), cancellation);
        Assert.Throws<OperationCanceledException>(() => NativePickleLoader.Load(stream, cancellationToken: cancellation.Token));
        Assert.True(stream.Position > 0);
    }

    [Fact]
    public async Task ConcurrentReadersUseIndependentDecoderState()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "sample.pkl");
        var batches = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => NativePickleLoader.Load(path))));
        Assert.All(batches, plans =>
        {
            Assert.Equal(2, plans.Count);
            Assert.Equal(13L, plans[0].Metadata["id"]);
            Assert.Equal(9, plans[0].SourceGraph!.Nodes.Count);
        });
        Assert.NotSame(batches[0][0].Geometries["living"][0], batches[1][0].Geometries["living"][0]);
    }

    private static List<Library.Plan> LoadRecords(int protocol, params Dictionary<string, object?>[] records)
    {
        var bytes = PickleSerializer.Dumps(records, protocol, new PicklerOptions { Registry = WriterRegistry() });
        return NativePickleLoader.Load(new MemoryStream(bytes));
    }

    private static WkbValue Shape(string wkt) => new(new WKTReader().Read(wkt));

    private static PickleRegistry WriterRegistry()
    {
        var registry = new PickleRegistry();
        registry.Register<WkbValue>("shapely.io", "from_wkb", (_, _) => throw new NotSupportedException(),
            value => new PickleReduction(new PickleGlobal("shapely.io", "from_wkb"), new PickleTuple([value.Geometry.AsBinary()])));
        registry.Register<DtypeValue>("numpy", "dtype", (_, _) => throw new NotSupportedException(),
            value => new PickleReduction(new PickleGlobal("numpy", "dtype"), new PickleTuple([value.Descriptor, false, true]),
                new PickleTuple([3L, value.ByteOrder, null, null, null, -1L, -1L, 0L])));
        registry.Register<ScalarValue>("numpy._core.multiarray", "scalar", (_, _) => throw new NotSupportedException(),
            value => new PickleReduction(new PickleGlobal("numpy._core.multiarray", "scalar"), new PickleTuple([value.Dtype, value.Bytes])));
        registry.Register<GraphValue>("networkx.classes.graph", "Graph", (_, _) => throw new NotSupportedException(),
            value => new PickleReduction(new PickleGlobal("networkx.classes.graph", "Graph"), PickleTuple.Empty, value.State));
        registry.Register<NodeViewValue>("networkx.classes.reportviews", "NodeView", (_, _) => throw new NotSupportedException(),
            value => new PickleReduction(new PickleGlobal("networkx.classes.reportviews", "NodeView"), PickleTuple.Empty, value.State));
        return registry;
    }

    private sealed record WkbValue(Geometry Geometry);
    private sealed record DtypeValue(string Descriptor, string ByteOrder);
    private sealed record ScalarValue(DtypeValue Dtype, byte[] Bytes);
    private sealed record GraphValue(Dictionary<string, object?> State);
    private sealed record NodeViewValue(Dictionary<string, object?> State);

    private sealed class CancelAfterReadStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override int ReadByte()
        {
            var value = base.ReadByte();
            cancellation.Cancel();
            return value;
        }
    }
}
