# ResPlan.NET

ResPlan.NET loads the ResPlan residential floorplan dataset, generates room connectivity graphs, renders PNGs, and saves plans with MessagePack. Pickle ingestion is implemented entirely in C# using [Pickle.Net](https://github.com/Xyrces/Pickle.Net). Running the library requires neither Python nor a virtual environment.

The library targets .NET 9. Tests run on .NET 9 and .NET 10. Rendering uses SkiaSharp; geometry operations use NetTopologySuite.

## Build from source

Pickle.Net is pinned as a Git submodule so a fresh checkout builds without requiring an unpublished NuGet dependency:

```sh
git clone --recurse-submodules https://github.com/Xyrces/resPlan.NET.git
cd resPlan.NET
dotnet restore ResPlan.slnx
dotnet build ResPlan.slnx -c Release
dotnet test ResPlan.Tests/ResPlan.Tests.csproj -c Release
```

Install the .NET 10 SDK and the .NET 9 runtime to run both test targets. For an existing checkout, run `git submodule update --init --recursive`. Applications can reference `ResPlan.Library/ResPlan.Library.csproj` directly.

To produce packages, build first, then pack both projects:

```sh
dotnet pack extern/Pickle.Net/src/Pickle.Net/Pickle.Net.csproj -c Release -o artifacts
dotnet pack ResPlan.Library/ResPlan.Library.csproj -c Release -o artifacts
```

The ResPlan.NET package depends on Pickle.Net 0.1.0. Consumers need both packages available from their configured NuGet sources, alongside NuGet.org for the other dependencies. CI builds both packages as downloadable artifacts. Registry publication is an explicit workflow dispatch from `main`, after Pickle.Net has been released from its own repository; a dependency check prevents publishing an unusable ResPlan.NET package. Pull requests never publish packages. Previously published ResPlan.NET versions may still use Python.

## Load a dataset

```csharp
using ResPlan.Library;

var plans = await PlanLoader.LoadPlansAsync(
    pklPathOverride: "/data/ResPlan.pkl",
    maxItems: 10,
    logger: Console.WriteLine);

foreach (var plan in plans)
{
    Console.WriteLine($"Index {plan.Id}, source ID {plan.Metadata["id"]}");
    Console.WriteLine($"{plan.Geometries.Count} geometry categories");
}
```

Omitting both paths downloads the pinned dataset revision, verifies its SHA-256, and caches `ResPlan/ResPlan.pkl`. Existing local datasets are respected. An explicit missing path throws instead of triggering an unexpected download. To load an existing JSON export, pass `jsonPath` or call `PlanLoader.LoadPlans(path)`.

`maxItems` selects the first records **before** applying constraints, consistently for JSON and pickle. Zero returns an empty list without downloading. Negative limits throw. Pickle stores a single object graph: limiting results does not avoid decoding the complete file or its memory cost.

Cancellation is available through an overload:

```csharp
var plans = await PlanLoader.LoadPlansAsync(
    cancellationToken: cancellationToken,
    pklPathOverride: "/data/ResPlan.pkl",
    maxItems: 100);
```

## Geometry, metadata, and graphs

- `Plan.Id` remains the zero-based input index for compatibility. The original dataset identifier is retained in `Plan.Metadata["id"]`.
- `Geometries` preserves every geometry category, including auxiliary categories, empty categories, and the neighboring geometries stored in tuples. Multipart values are expanded into their component geometries. `balacony` is normalized when `balcony` is absent; `stair` also supplies a `stairs` alias.
- `Metadata` retains nongeometry fields such as `unitType`, `area`, `net_area`, and `wall_depth`. It supports scalar values, byte buffers, arrays, and dictionaries with string keys. Unsupported values cause a descriptive error rather than disappearing.
- `WallWidth` controls graph buffering and defaults to `0.1`. A zero value uses the same fallback. `wall_depth` remains separate metadata; it is not substituted for `wall_width`.
- `ReferenceGraph` is generated from the plan geometry, matching the former loader's behavior. It is not an independent ground-truth graph. When a pickle contains a NetworkX graph, `SourceGraph` retains it separately, including node, edge, and graph attributes.
- Stored graph IDs retain the original Python/NumPy scalar spelling. Numerically equal edge endpoints (such as `1`, `1.0`, and `True`) resolve to the original node before conversion. Distinct nodes whose IDs genuinely collide after string conversion are rejected.
- `Bounds` covers visible rooms, walls, openings, and semantic anchor categories, with a fallback to all geometry for plans without those categories. Auxiliary neighboring plots do not normally expand the viewport.

```csharp
using ResPlan.Library;
using ResPlan.Library.Data;

var plan = plans[0];
var graph = GraphGenerator.GenerateGraph(plan);
var entrance = plan.GetEntrance();
var verticalAnchors = plan.GetVerticalAnchors();

PlanRenderer.Render(plan, "floorplan.png", width: 1200, height: 800);
PlanSerializer.SaveToFile(plan, "floorplan.msgpack");
var restored = PlanSerializer.LoadFromFile("floorplan.msgpack");
```

Rendering preserves aspect ratio on square, portrait, and landscape canvases. Graph generation and rendering traverse multipart geometries. MessagePack preserves geometry SRIDs, Z/M coordinates, metadata, and stored graphs; legacy two-dimensional WKB payloads remain readable.

To filter by a containing polygon, pass `constraints: new PlanGenerationConstraints { BoundingPolygon = polygon }`. Boundary contact is allowed. Constraints use the visible and semantic anchor categories described above, falling back to all geometry when those categories are empty. Neighboring plots do not affect containment when visible plan geometry is present.

## Input compatibility and limits

The native adapter explicitly supports Shapely WKB geometry, NumPy scalar numbers/booleans/strings and dtypes, and NetworkX `Graph`/`NodeView` state. NumPy 1.x and 2.x scalar module names are recognized. Arbitrary Python classes, NumPy arrays/structured dtypes, and other NetworkX graph classes require additional adapters and are rejected.

Pickle.Net never imports Python modules or executes Python code. Its constructor registry is an allowlist. The loader also limits input to 512 MiB, memo entries to 2,000,000, and individual pickle blobs/frames to 64 MiB. These limits do not make geometry processing or deserialization a general security sandbox. Process datasets within appropriate memory/time budgets.

Legacy JSON exports containing bare or quoted `NaN`/`Infinity` are accepted: nonfinite reference-node areas become zero and invalid bounds become an empty envelope. MessagePack can represent nonfinite metadata values directly.

## Validation

The ordinary test suite is fully native and uses committed fixtures. It covers pickle protocols 0–5, NumPy byte order, NetworkX reconstruction, unknown-global rejection, malformed inputs, limits/cancellation, JSON behavior, graph topology, image comparisons, multipart rendering, and MessagePack compatibility.

An independent Python oracle is retained **only for development verification**. See [DOCUMENTATION.md](DOCUMENTATION.md) for full-dataset comparison commands, the pinned dataset hashes, packaging details, and migration notes. Python helper files are not shipped in the library package.

## Attribution

This library adapts [ResPlan](https://github.com/m-agour/ResPlan), developed by Mohamed Abouagour and Eleftherios Garyfallidis. Please cite their work when using the dataset in research:

```bibtex
@article{AbouagourGaryfallidis2025ResPlan,
  title   = {ResPlan: A Large-Scale Vector-Graph Dataset of 17,000 Residential Floor Plans},
  author  = {Abouagour, Mohamed and Garyfallidis, Eleftherios},
  journal = {arXiv preprint arXiv:2508.14006},
  year    = {2025},
  doi     = {10.48550/arXiv.2508.14006},
  url     = {https://arxiv.org/abs/2508.14006}
}
```

## License

The ResPlan.NET code is licensed under the [MIT License](LICENSE). The dataset has separate terms: the [Kaggle distribution](https://www.kaggle.com/datasets/resplan/resplan) identifies it as CC BY-NC-SA 4.0. Consult the dataset distributor's terms before using or redistributing dataset files or derived data.
