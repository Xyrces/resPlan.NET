#!/usr/bin/env python3
"""Regenerate trusted graph fixtures near rounded buffer boundaries.

Expected graphs come from the unchanged Python reference. Every positive case
loses its edge when Shapely uses eight instead of sixteen segments per quadrant;
negative cases sit just outside the sixteen-segment boundary. Tests need .NET
only and never invoke this generator or unpickle external input in Python.
"""

from __future__ import annotations

import base64
import hashlib
import json
import math
import pickle
import platform
import sys
from pathlib import Path
from unittest.mock import patch

import networkx as nx
import shapely
from shapely.geometry import LineString, box
from shapely.geometry.base import BaseGeometry

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from resplan_utils import plan_to_graph  # noqa: E402


def corner_connector(radius):
    # Halfway between the first two vertices of an eight-segment quadrant.
    x = 1 + radius * math.cos(math.pi / 32)
    y = 1 + radius * math.sin(math.pi / 32)
    return LineString([(x, y), (x, y + 0.001)])


def cases():
    for connected, radius in ((True, 0.0748), (False, 0.0752)):
        yield f"direct_{connected}", connected, {
            "wall_width": 0.1,
            "living": box(0, 0, 1, 1),
            "front_door": corner_connector(radius),
        }

    for room_type in ("kitchen", "bedroom"):
        for connected, radius in ((True, 0.2248), (False, 0.2252)):
            # Adjacency buffers the room twice and living once: total radius .225.
            x = 1 + radius * math.cos(math.pi / 32)
            y = 1 + radius * math.sin(math.pi / 32)
            yield f"adjacency_{room_type}_{connected}", connected, {
                "wall_width": 0.1,
                "living": box(0, 0, 1, 1),
                room_type: box(x, y, x + 1, y + 1),
            }

    for source, target, connector in (("bathroom", "living", "door"), ("balcony", "bedroom", "window")):
        for side in ("source", "target"):
            for connected, radius in ((True, 0.0748), (False, 0.0752)):
                boundary_room = box(0, 0, 1, 1)
                surrounding_room = box(1.05, 0.99, 1.15, 1.09)
                yield f"via_{connector}_{side}_{connected}", connected, {
                    "wall_width": 0.1,
                    source: boundary_room if side == "source" else surrounding_room,
                    target: surrounding_room if side == "source" else boundary_room,
                    connector: corner_connector(radius),
                }


def snapshot(graph):
    return {
        "nodes": sorted(graph.nodes),
        "edges": sorted([*sorted((u, v)), data["type"]] for u, v, data in graph.edges(data=True)),
    }


def main():
    original_buffer = BaseGeometry.buffer

    def coarse_buffer(geometry, distance, *args, **kwargs):
        kwargs["quad_segs"] = 8
        return original_buffer(geometry, distance, *args, **kwargs)

    fixtures = []
    for name, connected, record in cases():
        expected = snapshot(plan_to_graph(record))
        assert len(expected["edges"]) == int(connected), name
        with patch.object(BaseGeometry, "buffer", coarse_buffer):
            coarse = snapshot(plan_to_graph(record))
        assert coarse["nodes"] == expected["nodes"], name
        assert coarse["edges"] == [], name
        payload = pickle.dumps([record], protocol=5)
        restored = pickle.loads(payload)[0]
        assert snapshot(plan_to_graph(restored)) == expected, name
        fixtures.append({
            "name": name,
            "expected": expected,
            "payload": base64.b64encode(payload).decode("ascii"),
        })

    result = {
        "schema": "resplan-graph-buffer-boundaries-v1",
        "pickle_protocol": 5,
        "reference_quadrant_segments": 16,
        "coarse_quadrant_segments": 8,
        "generator_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "reference_sha256": hashlib.sha256((ROOT / "resplan_utils.py").read_bytes()).hexdigest(),
        "versions": {
            "python": platform.python_version(),
            "shapely": shapely.__version__,
            "geos": shapely.geos_version_string,
            "networkx": nx.__version__,
        },
        "cases": fixtures,
    }
    destination = ROOT / "ResPlan.Tests/Fixtures/GraphBufferBoundaries"
    destination.mkdir(parents=True, exist_ok=True)
    output = destination / "fixtures.json"
    output.write_text(json.dumps(result, indent=2, ensure_ascii=True, allow_nan=False) + "\n", encoding="utf-8")
    print(f"Wrote {len(fixtures)} cases ({output.stat().st_size} bytes) to {output}")


if __name__ == "__main__":
    main()
