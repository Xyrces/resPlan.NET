using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ResPlan.Library;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests;

public class LoaderRegressionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(10, 3)]
    public async Task JsonLimitMatchesPickleSelectionSemantics(int limit, int expected)
    {
        var plans = await PlanLoader.LoadPlansAsync(jsonPath: "resplan_samples.json", maxItems: limit);
        Assert.Equal(expected, plans.Count);
    }

    [Fact]
    public async Task LimitIsAppliedBeforeConstraints()
    {
        using var file = new TemporaryJson("""
            [{"id":0,"geometries":{"living":["POLYGON ((0 0,1 0,1 1,0 1,0 0))"]}},
             {"id":1,"geometries":{"living":["POLYGON ((10 10,11 10,11 11,10 11,10 10))"]}}]
            """);
        var constraints = new PlanGenerationConstraints
        {
            BoundingPolygon = (NetTopologySuite.Geometries.Polygon)new WKTReader().Read(
                "POLYGON ((9 9,12 9,12 12,9 12,9 9))")
        };
        Assert.Empty(await PlanLoader.LoadPlansAsync(jsonPath: file.Path, maxItems: 1, constraints: constraints));
        Assert.Single(await PlanLoader.LoadPlansAsync(jsonPath: file.Path, constraints: constraints));
    }

    [Fact]
    public async Task InvalidArgumentsDoNotFallBackToDownloading()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PlanLoader.LoadPlansAsync(maxItems: -1));
        await Assert.ThrowsAsync<FileNotFoundException>(() => PlanLoader.LoadPlansAsync(jsonPath: Guid.NewGuid()+".json"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => PlanLoader.LoadPlansAsync(pklPathOverride: Guid.NewGuid()+".pkl"));
        Assert.Empty(await PlanLoader.LoadPlansAsync(maxItems: 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlanLoader.LoadPlansAsync(cancellationToken: cancellation.Token));
    }

    [Fact]
    public void LegacyBareNamedNumbersAreSanitizedWithoutRewritingStrings()
    {
        using var file = new TemporaryJson("""
            [{"id":7,"bounds":[NaN,0,Infinity,-Infinity],
              "metadata":{"label":"NaN Infinity -Infinity", "escaped":"say \"NaN\"", "amount":1e3},
              "reference_graph":{"nodes":[{"id":"n","type":"room","area":NaN}],"edges":[]}}]
            """);
        var plan = Assert.Single(PlanLoader.LoadPlans(file.Path));
        Assert.True(plan.Bounds.IsNull);
        Assert.Equal(0, plan.ReferenceGraph!.Nodes["n"].Area);
        Assert.Equal("NaN Infinity -Infinity", plan.Metadata["label"]);
        Assert.Equal("say \"NaN\"", plan.Metadata["escaped"]);
        Assert.Equal(1000, Convert.ToDouble(plan.Metadata["amount"]));
        JsonSerializer.Serialize(plan);
    }

    [Fact]
    public void JsonMetadataAndWallWidthSurviveMessagePack()
    {
        using var file = new TemporaryJson("""
            [{"id":3,"wall_width":0.25,"metadata":{"source":"fixture","nested":{"count":2},"items":[1,true,null]},
              "geometries":{"stair":["POLYGON ((0 0,1 0,1 1,0 1,0 0))"]}}]
            """);
        var plan = Assert.Single(PlanLoader.LoadPlans(file.Path));
        var restored = PlanSerializer.Deserialize(PlanSerializer.Serialize(plan));
        Assert.Equal(0.25, restored.WallWidth);
        Assert.Equal("fixture", restored.Metadata["source"]);
        Assert.Single(restored.GetVerticalAnchors());
    }

    [Fact]
    public async Task ConcurrentJsonLoadsUseIndependentGeometryReaders()
    {
        var batches = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            PlanLoader.LoadPlansAsync(jsonPath: "resplan_samples.json")));
        Assert.All(batches, plans => Assert.Equal(3, plans.Count));
        Assert.All(batches, plans => Assert.All(plans, plan => Assert.NotEmpty(plan.Geometries)));
    }

    [Theory]
    [InlineData("POLYGON ((0 0,2 0,2 2,0 2,0 0))")]
    [InlineData("POLYGON ((0 0,2 0,2 1,1 1,1 2,0 2,0 0))")]
    public async Task ConstraintsUseTheSameFootprintForRectangularAndConcaveBounds(string boundary)
    {
        using var file = new TemporaryJson("""
            [{"id":0,"bounds":[0,0,2,2],"geometries":{
              "living":["POLYGON ((0 0,2 0,2 1,1 1,1 2,0 2,0 0))"],
              "neighbor":["POINT (100 100)"]}}]
            """);
        var constraints = new PlanGenerationConstraints { BoundingPolygon = (Polygon)new WKTReader().Read(boundary) };
        Assert.Single(await PlanLoader.LoadPlansAsync(jsonPath: file.Path, constraints: constraints));
    }

    [Fact]
    public async Task ConstraintsRecomputeBoundsRatherThanTrustingStaleJsonBounds()
    {
        using var file = new TemporaryJson("""
            [{"id":0,"bounds":[0,0,1,1],"geometries":{
              "living":["POLYGON ((100 100,101 100,101 101,100 101,100 100))"]}}]
            """);
        var constraints = new PlanGenerationConstraints
        {
            BoundingPolygon = (Polygon)new WKTReader().Read("POLYGON ((0 0,2 0,2 2,0 2,0 0))")
        };
        Assert.Empty(await PlanLoader.LoadPlansAsync(jsonPath: file.Path, constraints: constraints));
    }

    [Fact]
    public void ConstraintsFallBackToAuxiliaryLayersWhenVisibleGeometryIsEmpty()
    {
        var reader = new WKTReader();
        var plan = new Plan
        {
            Bounds = new Envelope(0, 1, 0, 1),
            Geometries = new()
            {
                ["living"] = [reader.Read("POLYGON EMPTY")],
                ["neighbor"] = [reader.Read("POINT (100 100)")]
            }
        };
        Assert.False(PlanLoader.IsPlanCompatible(plan, (Polygon)reader.Read("POLYGON ((0 0,2 0,2 2,0 2,0 0))")));
        Assert.True(PlanLoader.IsPlanCompatible(plan, (Polygon)reader.Read("POLYGON ((99 99,101 99,101 101,99 101,99 99))")));
    }

    [Fact]
    public void MissingJsonBoundsUseTheVisibleFootprint()
    {
        using var file = new TemporaryJson("""
            [{"id":0,"geometries":{"living":["POLYGON ((0 0,2 0,2 2,0 2,0 0))"],
              "neighbor":["POINT (100 100)"]}}]
            """);
        Assert.Equal(new Envelope(0, 2, 0, 2), Assert.Single(PlanLoader.LoadPlans(file.Path)).Bounds);
    }

    [Fact]
    public async Task NativeAsyncLoadReturnsBeforeWorkerCompletesAndAllowsCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var releaseWorker = new ManualResetEventSlim();
        var workerStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskReturned = new TaskCompletionSource<Task<List<Plan>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = Path.Combine(AppContext.BaseDirectory, "sample.pkl");

        // Pause at the worker's first progress notification, so neither file size nor machine
        // speed can make a synchronous implementation appear asynchronous by finishing early.
        var caller = new Thread(() =>
        {
            try
            {
                var load = PlanLoader.LoadPlansAsync(cancellation.Token, pklPathOverride: path, logger: message =>
                {
                    if (!message.StartsWith("Loading ", StringComparison.Ordinal)) return;
                    workerStarted.TrySetResult(Environment.CurrentManagedThreadId);
                    releaseWorker.Wait();
                });
                taskReturned.TrySetResult(load);
            }
            catch (Exception exception) { taskReturned.TrySetException(exception); }
        }) { IsBackground = true };
        caller.Start();
        try
        {
            // Timeouts only detect deadlocks; no elapsed-time performance threshold is asserted.
            var workerThread = await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var load = await taskReturned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(caller.ManagedThreadId, workerThread);
            Assert.False(load.IsCompleted);
            cancellation.Cancel();
            releaseWorker.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await load.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            releaseWorker.Set();
            caller.Join(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class TemporaryJson(string contents) : IDisposable
    {
        public string Path { get; } = Create(contents);
        private static string Create(string contents)
        {
            var path = System.IO.Path.GetTempFileName();
            File.WriteAllText(path, contents);
            return path;
        }
        public void Dispose() => File.Delete(Path);
    }
}
