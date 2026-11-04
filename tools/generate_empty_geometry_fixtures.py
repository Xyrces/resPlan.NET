#!/usr/bin/env python3
"""Regenerate trusted Shapely empty-geometry fixtures; tests need only .NET.

Each geometry is held in a real NetworkX node, whose SourceGraph geometry is
retained by the native loader. Python unpickles only payloads built here.
"""

from __future__ import annotations

import base64
import hashlib
import json
import pickle
import platform
from pathlib import Path

import networkx as nx
import shapely


def snapshot(geometry):
    return {
        "geometry_type": geometry.geom_type,
        "is_empty": bool(shapely.is_empty(geometry)),
        "dimension": int(shapely.get_coordinate_dimension(geometry)),
        "has_z": bool(shapely.has_z(geometry)),
        "has_m": bool(shapely.has_m(geometry)),
        "srid": int(shapely.get_srid(geometry)),
    }


def main():
    destination = Path(__file__).resolve().parents[1] / "ResPlan.Tests/Fixtures/EmptyGeometries"
    destination.mkdir(parents=True, exist_ok=True)
    fixtures = []
    for geometry_type in ("POINT", "POLYGON"):
        for schema, qualifier in (("xy", ""), ("xyz", "Z"), ("xym", "M"), ("xyzm", "ZM")):
            wkt = " ".join(part for part in (geometry_type, qualifier, "EMPTY") if part)
            geometry = shapely.set_srid(shapely.from_wkt(wkt), 4326)
            expected = snapshot(geometry)
            graph = nx.Graph()
            graph.add_node("empty", geometry=geometry)
            payloads = {}
            for protocol in range(6):
                payload = pickle.dumps([{"graph": graph}], protocol=protocol)
                restored = pickle.loads(payload)[0]["graph"].nodes["empty"]["geometry"]
                assert snapshot(restored) == expected, (wkt, protocol)
                payloads[str(protocol)] = base64.b64encode(payload).decode("ascii")
            fixtures.append({
                "name": f"{geometry_type.lower()}_{schema}",
                "wkt": wkt,
                "expected": expected,
                "payloads": payloads,
            })
    result = {
        "schema": "resplan-shapely-empty-geometries-v1",
        "generator_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "versions": {
            "python": platform.python_version(),
            "shapely": shapely.__version__,
            "geos": shapely.geos_version_string,
            "networkx": nx.__version__,
        },
        "cases": fixtures,
    }
    output = destination / "fixtures.json"
    output.write_text(json.dumps(result, indent=2, ensure_ascii=True, allow_nan=False) + "\n", encoding="utf-8")
    print(f"Wrote {len(fixtures)} cases x 6 protocols ({output.stat().st_size} bytes) to {output}")


if __name__ == "__main__":
    main()
