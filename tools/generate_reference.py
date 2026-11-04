#!/usr/bin/env python3
"""Create an independent Shapely/NetworkX oracle from a TRUSTED ResPlan pickle.

Python pickle can execute code. Use only the checked-in sample or the pinned,
trusted dataset. The native validator checks the input SHA-256 in this oracle.
Generated room graphs come directly from the repository's Python reference.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import math
import pickle
from pathlib import Path
import platform
import sys
import time

import networkx as nx
import numpy as np
import shapely
from shapely.geometry.base import BaseGeometry

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from resplan_utils import plan_to_graph  # noqa: E402


# Bounds retain the historical nine visible layers, plus supported anchor layers.
BOUND_LAYERS = {
    "living", "bedroom", "bathroom", "kitchen", "door", "window", "wall",
    "front_door", "balcony", "stairs", "stair", "elevator", "foyer",
    "corridor", "entrance",
}


def geometry_parts(value):
    """Recognize geometry-valued fields without losing empty layers or tuple parts."""
    if isinstance(value, BaseGeometry):
        if value.is_empty:
            return []
        if hasattr(value, "geoms"):
            return [part for child in value.geoms for part in geometry_parts(child)]
        return [value]
    if isinstance(value, (tuple, list)):
        children = [geometry_parts(child) for child in value if child is not None]
        if children and all(parts is not None for parts in children):
            return [part for parts in children for part in parts]
    return None


def geometry_hash(geometry):
    if geometry is None:
        return None
    # No canonicalization: vertex order, ring order, exact XY doubles and geometry
    # type must survive. Both implementations explicitly write little-endian 2D WKB.
    return hashlib.sha256(shapely.to_wkb(
        geometry, byte_order=1, output_dimension=2, include_srid=False
    )).hexdigest()


def metadata(value):
    if isinstance(value, np.generic):
        return metadata(value.item())
    if value is None or isinstance(value, (str, bool, int)):
        return value
    if isinstance(value, float):
        if math.isfinite(value):
            return value
        return {"$float": "nan" if math.isnan(value) else ("inf" if value > 0 else "-inf")}
    if isinstance(value, (bytes, bytearray, memoryview)):
        return {"$bytes": base64.b64encode(value).decode("ascii")}
    if isinstance(value, dict):
        if not all(isinstance(key, str) for key in value):
            raise TypeError("Metadata dictionary keys must be strings")
        return {key: metadata(child) for key, child in value.items()}
    if isinstance(value, (list, tuple)):
        return [metadata(child) for child in value]
    raise TypeError(f"Unsupported oracle metadata: {type(value).__name__}")


def graph_snapshot(graph):
    if graph is None:
        return None
    if not isinstance(graph, nx.Graph) or graph.is_directed() or graph.is_multigraph():
        raise TypeError("Expected an undirected NetworkX Graph")
    nodes = {}
    identifiers = {}
    nan_identifier = None
    for key, attrs in graph.nodes(data=True):
        geometry = attrs.get("geometry")
        identifier = str(key)
        if identifier in nodes:
            raise ValueError(f"Node IDs collide after string conversion: {identifier}")
        identifiers[key] = identifier
        if isinstance(key, (float, np.floating)) and math.isnan(key):
            nan_identifier = identifier
        area = attrs.get("area")
        nodes[identifier] = {
            "id": identifier,
            "type": "" if attrs.get("type") is None else str(attrs["type"]),
            "area": metadata(float(area) if area is not None else (geometry.area if geometry is not None else 0.0)),
            "geometry": geometry_hash(geometry),
            "metadata": metadata({key: value for key, value in attrs.items() if key not in {"geometry", "type", "area"}}),
        }
    def canonical_identifier(key):
        # Resolve aliases (1, 1.0, True, NumPy scalars) using Python dictionary
        # equality before converting the node's original key to its public ID.
        if key in identifiers:
            return identifiers[key]
        # Built-in NaN floats are not memoized by pickle. Only a single numeric
        # NaN node is representable by the public string IDs checked above.
        if nan_identifier is not None and isinstance(key, (float, np.floating)) and math.isnan(key):
            return nan_identifier
        raise ValueError(f"Edge endpoint is absent from the node dictionary: {key!r}")

    edges = []
    visited = set()
    for source, target, attrs in graph.edges(data=True):
        # UTF-16 ordinal ordering matches the public .NET string ID convention.
        source, target = sorted((canonical_identifier(source), canonical_identifier(target)),
                                key=lambda text: text.encode("utf-16-be", "surrogatepass"))
        if (source, target) in visited:
            continue
        visited.add((source, target))
        edges.append({
            "source": source,
            "target": target,
            "type": "" if attrs.get("type") is None else str(attrs["type"]),
            "metadata": metadata({key: value for key, value in attrs.items() if key != "type"}),
        })
    edges.sort(key=lambda edge: tuple(edge[key].encode("utf-16-be", "surrogatepass") for key in ("source", "target", "type")))
    return {"nodes": nodes, "edges": edges, "metadata": metadata(graph.graph)}


def plan_snapshot(record, ordinal):
    geometry_layers = {}
    attributes = {}
    for key, value in record.items():
        if key == "graph":
            continue
        parts = geometry_parts(value)
        if parts is None:
            attributes[key] = metadata(value)
        else:
            category = "balcony" if key == "balacony" and "balcony" not in record else key
            geometry_layers[category] = parts
    if "stair" in geometry_layers and "stairs" not in geometry_layers:
        geometry_layers["stairs"] = geometry_layers["stair"]

    visible = [part for category, parts in geometry_layers.items() if category in BOUND_LAYERS for part in parts]
    bounded = visible or [part for parts in geometry_layers.values() for part in parts]
    bounds = shapely.total_bounds(bounded).tolist() if bounded else None
    width = record.get("wall_width")
    width = 0.1 if width is None else float(width)
    return {
        "id": ordinal,
        "geometries": {category: [geometry_hash(part) for part in parts] for category, parts in geometry_layers.items()},
        "metadata": attributes,
        "bounds": bounds,
        "wall_width": metadata(width),
        "reference_graph": graph_snapshot(plan_to_graph(dict(record))),
        "source_graph": graph_snapshot(record.get("graph")),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--max-plans", type=int)
    parser.add_argument("--progress-every", type=int, default=1000)
    args = parser.parse_args()
    if args.max_plans is not None and args.max_plans < 0:
        parser.error("--max-plans must be nonnegative")
    started = time.perf_counter()
    with args.input.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
        source.seek(0)
        plans = pickle.load(source)
    if not isinstance(plans, list):
        raise TypeError("Expected a list of plans")
    count = min(len(plans), args.max_plans if args.max_plans is not None else len(plans))
    manifest = {
        "schema": "resplan-native-oracle-v1",
        "input_name": args.input.name,
        "input_bytes": args.input.stat().st_size,
        "input_sha256": digest,
        "reference_source_sha256": hashlib.sha256((Path(__file__).resolve().parents[1] / "resplan_utils.py").read_bytes()).hexdigest(),
        "source_plan_count": len(plans),
        "plan_count": count,
        "versions": {"python": platform.python_version(), "shapely": shapely.__version__, "geos": shapely.geos_version_string, "networkx": nx.__version__, "numpy": np.__version__},
        "geometry_encoding": "sha256 of little-endian 2D WKB; no coordinate normalization",
        "area_absolute_tolerance": 1e-10,
        "area_relative_tolerance": 1e-12,
    }
    stats = {"plans": count, "geometries": 0, "empty_layers": 0, "source_graphs": 0, "nodes": 0, "edges": 0}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="\n") as output:
        output.write(json.dumps(manifest, ensure_ascii=True, allow_nan=False, separators=(",", ":")) + "\n")
        for ordinal, record in enumerate(plans[:count]):
            try:
                expected = plan_snapshot(record, ordinal)
            except Exception as exception:
                raise RuntimeError(f"Oracle failed at ordinal {ordinal}, raw id {record.get('id')}") from exception
            output.write(json.dumps(expected, ensure_ascii=True, allow_nan=False, separators=(",", ":")) + "\n")
            stats["geometries"] += sum(len(parts) for parts in expected["geometries"].values())
            stats["empty_layers"] += sum(not parts for parts in expected["geometries"].values())
            stats["source_graphs"] += expected["source_graph"] is not None
            stats["nodes"] += len(expected["reference_graph"]["nodes"])
            stats["edges"] += len(expected["reference_graph"]["edges"])
            if args.progress_every > 0 and (ordinal + 1) % args.progress_every == 0:
                print(f"Oracle: {ordinal + 1}/{count} plans, {time.perf_counter() - started:.1f}s", file=sys.stderr, flush=True)
    print(json.dumps({**stats, "input_sha256": digest, "elapsed_seconds": round(time.perf_counter() - started, 3), "output_bytes": args.output.stat().st_size}), file=sys.stderr)


if __name__ == "__main__":
    main()
