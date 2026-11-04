# Architecture and migration notes

## Runtime flow

`PlanLoader` reads JSON directly or opens the pickle with `NativePickleLoader`. The latter uses Pickle.Net's stack-machine decoder and an explicit ResPlan constructor registry. Shapely WKB becomes NetTopologySuite geometry; NumPy scalar buffers become CLR values; NetworkX instance state becomes a separate `SourceGraph`. Unknown globals fail during decoding. There are no subprocesses, Python packages, or automatic runtime dependency installation.

The public five-argument `LoadPlansAsync` signature and synchronous JSON entry point remain available. A cancellation-token overload is added. The removed `PythonEnvManager` was an implementation-specific public helper; callers that explicitly invoked it should remove those calls. No replacement is required.

The previous Python bridge replaced source IDs with input indices and retained only nine geometry categories. The native loader retains the indices in `Id`, exposes the original identifier in metadata, and preserves all geometry categories. Semantic anchor categories now participate in bounds and constraints. Containment uses the same visible/anchor category selection as the native viewport, with an all-geometry fallback only when those categories are empty; neighboring plots do not inconsistently affect the precise check. `ReferenceGraph` continues to mean the graph generated from geometry; `SourceGraph` exposes the stored NetworkX graph when present.

`GraphGenerator` mirrors the local reference algorithm in `resplan_utils.py`, including its double-buffer step for room adjacency and per-plan `wall_width`. The graph uses room polygons and front-door geometries as nodes, with direct, adjacency, via-door, and via-window edges. Multipart connectors are evaluated individually. Rendering uses a uniform scale that fits both requested canvas dimensions.

## Dataset provenance

Automatic downloads use the immutable upstream revision:

- Repository: https://github.com/m-agour/ResPlan
- Revision: `e2b78fe069aee1ab1e1828a612743f308e3c32a7`
- Archive: `ResPlan.zip`, 100,106,537 bytes
- Archive SHA-256: `f718de8865e51bbe93b49b584798e3c536ed6b4b8a5d32f01b56812f389aeb46`
- Pickle: `ResPlan.pkl`, 258,453,658 bytes, 17,000 records
- Pickle SHA-256: `103edd854a5f365aa875ed832e6cef0d8bc72c4b34e5df0856c01b6970684cb5`

The pickle references Shapely's WKB constructor, NumPy's scalar constructor, and `numpy.dtype`. The older committed two-plan sample also references NetworkX `Graph` and `NodeView`. It therefore exercises adapters that the current complete dataset does not use.

Download verification happens before extraction. The loader only installs the expected pickle entry and uses temporary files before committing a completed download. Explicit local datasets are not forced to match the pinned dataset, but must satisfy the native adapter and resource limits.

## MessagePack

Existing integer keys 0–3 on `Plan` are retained. New keys are appended: 4=`WallWidth`, 5=`Metadata`, 6=`SourceGraph`. Metadata fields are also appended to graph/node/edge records. Old records receive model defaults. Geometry remains a MessagePack binary field containing WKB, now written as EWKB to retain SRID and XYZM data. Readers accept old WKB. Typed geometry formatters validate the requested type; standalone typed `LinearRing` values are reconstructed as rings.

MessagePack 3.1.12 and its `UntrustedData` protections are used. Standard resource protections do not replace application-level size/time limits for NetTopologySuite geometry processing.

## Packaging

`extern/Pickle.Net` is a Git submodule pinned to a reviewed commit. The project reference enables a clean source checkout to build without a published package. `dotnet pack` records a dependency on the Pickle.Net package; it does not copy its source or silently embed its assembly.

Build/package both projects. A package consumer needs a NuGet source containing **both** resulting packages, as well as NuGet.org for transitive dependencies. For a private local smoke test, a consumer-local `NuGet.Config` can declare the artifact directory and `https://api.nuget.org/v3/index.json`. No Python content is included in the ResPlan.NET package.

The separately dispatched **Full dataset compatibility** workflow downloads the pinned archive, verifies its digest, generates the independent Python oracle, and compares every record and MessagePack round trip. The normal CI matrix runs the committed fixture suite on Linux and Windows, on .NET 9 and .NET 10, and uploads both packages as artifacts. Registry publication requires a workflow dispatch on `main` with `publish` enabled. It first verifies that Pickle.Net 0.1.0 is available from the GitHub Packages feed, then publishes only ResPlan.NET. Pickle.Net must be released from its own repository: GitHub's repository-scoped workflow token cannot publish a package associated with a different repository. See [GitHub's NuGet authentication documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-to-github-packages). Fork pull requests require no package-write credentials or dataset download.

## Verification

Run the native suite with:

```sh
dotnet test ResPlan.Tests/ResPlan.Tests.csproj -c Release
```

The full-dataset oracle and native comparison utility live in `tools/`. They compare geometry coordinates through normalized WKB hashes, preserved metadata, bounds, generated graph topology/areas, and stored graph data. Full dataset outputs are local validation artifacts and are not committed. Python is used only to establish an independent reference for trusted fixture data; it is never invoked by the library or the native test suite.

The committed oracle for the two-plan sample also exercises stored NetworkX graphs. Compare it without Python:

```sh
dotnet run --project tools/ResPlan.Validation -c Release -- ResPlan.Tests/sample.pkl tools/fixtures/sample.expected.jsonl
```

For the complete dataset, install the development-only Python dependencies from `tools/requirements-validation.txt` into a virtual environment, then run:

```sh
python tools/generate_reference.py --input /data/ResPlan.pkl --output /tmp/resplan-expected.jsonl
dotnet run --project tools/ResPlan.Validation -c Release -- /data/ResPlan.pkl /tmp/resplan-expected.jsonl --report /tmp/resplan-validation.json
```

Only run the Python oracle on trusted data: Python's unpickler can execute code. The native comparison verifies the input hash against the oracle manifest and exits nonzero on mismatch. Coordinates and topology must match exactly; graph areas alone allow an absolute tolerance of `1e-10` plus a relative tolerance of `1e-12` for geometry-engine floating-point summation. Every validated plan also undergoes a MessagePack round trip with exact numeric comparison. The manifest records Python, Shapely, GEOS, NumPy and NetworkX versions; the report records counts, timings, memory usage and any differences.

Native pickle decoding runs on a worker thread so awaiting the loader does not block a UI thread. Logger callbacks may execute on that worker thread; UI callers should dispatch updates through their UI framework or an `IProgress<string>` implementation.
