#!/usr/bin/env python3
"""Regenerate trusted NetworkX identifier fixtures; Python is not needed by tests.

Run with NumPy and NetworkX installed. The fixtures contain only graphs built
here, never external pickle input. Every oracle is computed after CPython has
unpickled its own payload, so protocol-specific identity behavior is checked.
"""

from __future__ import annotations

import base64
import hashlib
import json
import pickle
import platform
from pathlib import Path

import networkx as nx
import numpy as np


def graph_with_alias(canonical, alias, string_node=None):
    graph = nx.Graph()
    graph.add_node(canonical)
    if string_node is not None:
        graph.add_node(string_node)
    graph.add_edge(alias, "peer")
    return graph


def cases():
    yield "integer_float_alias", graph_with_alias(1, 1.0)
    yield "integer_float_alias_with_string", graph_with_alias(1, 1.0, "1.0")
    yield "float_integer_alias_with_string", graph_with_alias(1.0, 1, "1")
    yield "boolean_integer_alias", graph_with_alias(True, 1, "1")
    yield "boolean_float_alias", graph_with_alias(True, 1.0, "1.0")
    yield "integer_boolean_alias", graph_with_alias(1, True, "True")
    yield "negative_zero_positive_alias", graph_with_alias(-0.0, 0.0, "0.0")
    yield "positive_zero_negative_alias", graph_with_alias(0.0, -0.0, "-0.0")
    yield "integer_negative_zero_alias", graph_with_alias(0, -0.0, "-0.0")

    graph = nx.Graph()
    graph.add_nodes_from([1, 2])
    graph.add_edge(1.0, 2.0)
    yield "both_edge_endpoints_are_aliases", graph

    # Python dictionaries may legally retain different equal key objects in
    # each adjacency dictionary. NetworkX resolves both through key equality.
    graph = nx.Graph()
    graph.add_nodes_from([1, 2])
    graph.add_edge(1.0, 2.0)
    graph._adj = {float(key): neighbors for key, neighbors in graph._adj.items()}
    yield "outer_and_inner_adjacency_aliases", graph

    for power in (53, 100):
        graph = nx.Graph()
        graph.add_nodes_from([2**power, 2**power + 1])
        graph.add_edge(float(2**power), "peer")
        yield f"distinct_integers_around_2pow{power}", graph

    for name, dtype in (("float16", np.float16), ("float32", np.float32)):
        scalar = dtype(0.1)
        widened = float(scalar)
        yield f"numpy_{name}_precision", graph_with_alias(scalar, scalar, str(widened))
        yield f"numpy_{name}_builtin_alias", graph_with_alias(scalar, widened, str(widened))
        yield f"builtin_numpy_{name}_alias", graph_with_alias(widened, scalar, str(scalar))
        yield f"numpy_{name}_integer_alias", graph_with_alias(dtype(1), 1, "1")
        yield f"numpy_{name}_negative_zero_alias", graph_with_alias(dtype(-0.0), 0, "0")
        for suffix, value in (
            ("small", dtype(1e-5)),
            ("negative", dtype(-0.1)),
            ("lower_threshold_below", np.nextafter(dtype(1e-4), dtype(0))),
            ("lower_threshold_at", dtype(1e-4)),
            ("lower_threshold_above", np.nextafter(dtype(1e-4), dtype("inf"))),
            ("upper_threshold_below", np.nextafter(dtype(1000 if name == "float16" else 1e6), dtype(0))),
            ("upper_threshold_at", dtype(1000 if name == "float16" else 1e6)),
            ("upper_threshold_above", np.nextafter(dtype(1000 if name == "float16" else 1e6), dtype("inf"))),
            ("max", np.finfo(dtype).max),
            ("subnormal", np.nextafter(dtype(0), dtype(1))),
            ("positive_infinity", dtype("inf")),
            ("negative_infinity", dtype("-inf")),
            ("nan", dtype("nan")),
        ):
            yield f"numpy_{name}_{suffix}", graph_with_alias(value, value)

    yield "numpy_boolean_integer_alias", graph_with_alias(np.bool_(True), 1, "1")
    yield "numpy_unsigned_integer_alias", graph_with_alias(np.uint64(2**63), 2**63, "peer-id")


def invalid_cases():
    for name, key, string_key in (("missing_float_node", 1.0, "1.0"), ("missing_nan_node", float("nan"), "nan")):
        graph = nx.Graph()
        graph.add_edge("peer", string_key)
        # The string node exists, but a numeric key with the same string
        # representation is absent. Its adjacency must never bind by spelling.
        graph._adj["peer"] = {key: graph._adj["peer"][string_key]}
        yield name, graph, KeyError, "NetworkX edge"
    for name, dtype in (("float16", np.float16), ("float32", np.float32)):
        scalar = dtype(0.1)
        yield f"numpy_{name}_genuine_string_collision", graph_with_alias(scalar, scalar, str(scalar)), ValueError, "node IDs collide after conversion"


def decorate(graph):
    graph.graph["measurement"] = np.float32(0.1)
    for attributes in graph._node.values():
        attributes.update(area=np.float16(0.1), type=np.float32(0.1), measurement=np.float16(0.1))
    for neighbors in graph._adj.values():
        for attributes in neighbors.values():
            attributes.update(type=np.float16(0.1), measurement=np.float32(0.1))
    return {
        "graph": graph,
        "wall_width": np.float16(0.1),
        "float16": np.float16(0.1),
        "float32": np.float32(0.1),
        "nested": {"values": [np.float16(0.1), np.float32(0.1), np.float64(0.1)]},
    }


def snapshot(record):
    graph = record["graph"]
    canonical = {key: str(key) for key in graph._node}
    if len(set(canonical.values())) != len(canonical):
        raise ValueError("Fixture contains a genuine string-ID collision")
    edges = set()
    for source, neighbors in graph._adj.items():
        for target in neighbors:
            # Consult the node dictionary with Python's identity/equality
            # semantics. str(target) alone can name an unrelated string node.
            edges.add(tuple(sorted((canonical[source], canonical[target]))))
    return {
        "nodes": sorted(canonical.values()),
        "edges": sorted(edges),
        "float16": float(record["float16"]),
        "float32": float(record["float32"]),
        "node_type": str(np.float32(0.1)),
        "edge_type": str(np.float16(0.1)),
    }


def main():
    destination = Path(__file__).resolve().parents[1] / "ResPlan.Tests/Fixtures/GraphIdentifiers"
    destination.mkdir(parents=True, exist_ok=True)
    fixtures = []
    for name, graph in cases():
        record = decorate(graph)
        expected = snapshot(record)
        payloads = {}
        for protocol in range(6):
            payload = pickle.dumps([record], protocol=protocol)
            restored = pickle.loads(payload)[0]
            assert snapshot(restored) == expected, (name, protocol)
            payloads[str(protocol)] = base64.b64encode(payload).decode("ascii")
        fixtures.append({"name": name, "expected": expected, "payloads": payloads})
    for name, graph, error_type, expected_error in invalid_cases():
        record = decorate(graph)
        payloads = {}
        for protocol in range(6):
            payload = pickle.dumps([record], protocol=protocol)
            restored = pickle.loads(payload)[0]
            try:
                snapshot(restored)
            except error_type:
                pass
            else:
                raise AssertionError(f"Malformed fixture unexpectedly accepted: {name}, protocol {protocol}")
            payloads[str(protocol)] = base64.b64encode(payload).decode("ascii")
        fixtures.append({"name": name, "expected_error": expected_error, "payloads": payloads})
    result = {
        "schema": "resplan-networkx-identifiers-v1",
        "generator_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "versions": {"python": platform.python_version(), "numpy": np.__version__, "networkx": nx.__version__},
        "cases": fixtures,
    }
    output = destination / "fixtures.json"
    output.write_text(json.dumps(result, indent=2, ensure_ascii=True, allow_nan=False) + "\n", encoding="utf-8")
    print(f"Wrote {len(fixtures)} cases x 6 protocols ({output.stat().st_size} bytes) to {output}")


if __name__ == "__main__":
    main()
