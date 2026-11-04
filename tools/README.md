# Native interoperability validation

`ResPlan.Validation` compares the public native loader with an independent
Shapely/NetworkX oracle and checks a MessagePack round trip for every plan.
The checked-in sample oracle covers both stored NetworkX graphs and generated
graphs. Running this comparison requires .NET only:

```bash
dotnet run --project tools/ResPlan.Validation -c Release -- \
  ResPlan.Tests/sample.pkl tools/fixtures/sample.expected.jsonl
```

The validator exits `0` on success, `1` for comparison failures, and `2` for
invalid arguments, input/oracle errors, or load failures. `--report FILE.json`
also saves its summary, including counts, timings, peak working set, and the
first 30 mismatches. All mismatches are counted even when examples are capped.
An input SHA-256 mismatch fails before decoding the pickle.

## Regenerating the oracle

Python is a development-time reference, not a runtime library dependency.
Only load trusted files with the Python generator: Python pickle can execute
code. Install the pinned reference dependencies and regenerate the sample:

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r tools/requirements-validation.txt
.venv/bin/python tools/generate_reference.py \
  --input ResPlan.Tests/sample.pkl \
  --output tools/fixtures/sample.expected.jsonl
```

The manifest records interpreter/library versions, input digest, full source
count, selected count, and numeric tolerances. `--max-plans N` generates an
explicitly labeled prefix for debugging. The validator reports that prefix
and the original total separately.

For the full trusted dataset, keep the large input and generated oracle outside
the repository. The validated 17,000-plan `ResPlan.pkl` is 258,453,658 bytes with
SHA-256 `103edd854a5f365aa875ed832e6cef0d8bc72c4b34e5df0856c01b6970684cb5`:

```bash
.venv/bin/python tools/generate_reference.py \
  --input /tmp/resplan-native-validation/ResPlan.pkl \
  --output /tmp/resplan-native-validation/expected.jsonl
dotnet run --project tools/ResPlan.Validation -c Release -- \
  /tmp/resplan-native-validation/ResPlan.pkl \
  /tmp/resplan-native-validation/expected.jsonl \
  --report /tmp/resplan-native-validation/native-report.json
```

## Comparison contract

- Every geometry category is checked, including empty layers, auxiliary layers,
  and geometry sequences such as nested `neighbor` tuples. Multipart geometry
  is flattened in source order. The `balacony` typo and `stair` alias follow the
  public loader contract.
- SHA-256 hashes of little-endian 2D WKB check exact XY coordinates, geometry
  types, vertex order, and ring order. No geometric normalization or tolerance
  is applied. Z, M, and SRID preservation has separate library unit tests.
- Bounds cover the nine historical visible categories plus supported semantic
  anchor categories, falling back to all geometry when those are empty.
- `Plan.Id` is the legacy ordinal; original IDs and other metadata remain in
  `Metadata`. Metadata values and bounds are compared without tolerance.
- Generated graphs come from `resplan_utils.plan_to_graph` without substituting
  the C# algorithm. Node IDs/types/geometries, undirected typed edges, stored
  source graphs, and graph attributes are compared. Only computed node areas
  permit `1e-10 + 1e-12 * max(abs(expected), abs(actual))` error because GEOS and
  NetTopologySuite can sum polygon areas in different orders.
- MessagePack round trips use the same complete snapshot with **zero numeric
  tolerance**, including graph areas. These checks run on every selected plan.
