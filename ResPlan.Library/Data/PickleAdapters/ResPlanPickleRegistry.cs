using System.Collections;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Pickle.Net;

namespace ResPlan.Library.Data.PickleAdapters;

/// <summary>The complete allowlist of Python constructors understood by the dataset reader.</summary>
internal static class ResPlanPickleRegistry
{
    internal static PickleRegistry Create()
    {
        var registry = new PickleRegistry();
        // Each load owns its readers and registry; concurrent callers share no mutable decoder state.
        var wkb = new WKBReader();
        registry.Register<Geometry>("shapely.io", "from_wkb", (args, keywords) =>
        {
            RequireArguments(args, keywords, 1, 1, "shapely.io.from_wkb");
            try { return wkb.Read(Bytes(args[0], "Shapely WKB")); }
            catch (Exception exception) when (exception is ArgumentException or ParseException or EndOfStreamException)
            { throw new UnpicklingException("The Shapely constructor contains invalid WKB geometry.", exception); }
        });

        registry.Register<NumpyDtype>("numpy", "dtype", NumpyDtype.Create,
            applyState: (dtype, state) => dtype.ApplyState(state));
        registry.Register<object>("numpy._core.multiarray", "scalar", NumpyDtype.ReadScalar);
        // NumPy 1.x used the public `core` spelling; the data format is otherwise identical.
        registry.Register<object>("numpy.core.multiarray", "scalar", NumpyDtype.ReadScalar);

        registry.Register<NetworkXGraph>("networkx.classes.graph", "Graph", (args, keywords) =>
        {
            RequireArguments(args, keywords, 0, 0, "networkx.classes.graph.Graph");
            return new NetworkXGraph();
        }, applyState: (graph, state) => graph.State = StateDictionary(state, "NetworkX Graph"));
        registry.Register<NetworkXNodeView>("networkx.classes.reportviews", "NodeView", (args, keywords) =>
        {
            RequireArguments(args, keywords, 0, 0, "networkx.classes.reportviews.NodeView");
            return new NetworkXNodeView();
        }, applyState: (view, state) => view.State = StateDictionary(state, "NetworkX NodeView"));
        return registry;
    }

    internal static void RequireArguments(PickleTuple arguments, PickleDictionary? keywords,
        int minimum, int maximum, string constructor)
    {
        if (arguments.Count < minimum || arguments.Count > maximum || keywords is { Count: > 0 })
            throw new UnpicklingException($"Unsupported arguments for {constructor}.");
    }

    internal static byte[] Bytes(object? value, string description) => value switch
    {
        byte[] bytes => bytes,
        PickleByteArray bytes => bytes.Data,
        PickleBuffer bytes => bytes.Data.ToArray(),
        _ => throw new UnpicklingException($"{description} must contain a byte buffer.")
    };

    internal static IDictionary StateDictionary(object? state, string description)
    {
        if (state is IDictionary dictionary) return dictionary;
        // Accommodate Python's standard (instance dictionary, slot dictionary) BUILD state.
        if (state is PickleTuple { Count: 2 } tuple && tuple[0] is null && tuple[1] is IDictionary slots)
            return slots;
        throw new UnpicklingException($"{description} has an unsupported pickle state.");
    }
}

internal sealed class NetworkXGraph
{
    internal IDictionary State { get; set; } = new PickleDictionary();
}

internal sealed class NetworkXNodeView
{
    internal IDictionary State { get; set; } = new PickleDictionary();
}
