import pickle
import json
import os
import sys
import networkx as nx
import matplotlib.pyplot as plt
from shapely.geometry import mapping
from shapely.wkt import dumps as wkt_dumps
import numpy as np

# Add local path to import resplan_utils
sys.path.append('ResPlan')
from resplan_utils import plan_to_graph, plot_plan, normalize_keys, get_geometries

DATA_PATH = 'ResPlan/ResPlan.pkl'
OUTPUT_JSON = 'resplan_samples.json'
IMG_DIR = 'reference_images'

os.makedirs(IMG_DIR, exist_ok=True)

def shapely_to_wkt(geom):
    if geom is None:
        return None
    if isinstance(geom, list):
        return [shapely_to_wkt(g) for g in geom]
    return wkt_dumps(geom)

def analyze_types(plan):
    types = {}
    for k, v in plan.items():
        if k in ['graph', 'id', 'unitType', 'area', 'net_area', 'land']: continue
        geoms = get_geometries(v)
        if geoms:
            types[k] = type(geoms[0]).__name__
    return types

def main():
    print(f"Loading {DATA_PATH}...")
    with open(DATA_PATH, 'rb') as f:
        plans = pickle.load(f)

    print(f"Loaded {len(plans)} plans.")

    samples = []
    # Select a few indices. Using 0, 10, 50 as arbitrary samples.
    indices = [0, 10, 50]

    for idx in indices:
        if idx >= len(plans): continue
        print(f"Processing plan {idx}...")
        plan = plans[idx]
        normalize_keys(plan)

        # Analyze types
        geom_types = analyze_types(plan)
        print(f"  Geometry Types: {geom_types}")

        # Generate Graph
        G = plan_to_graph(plan)
        # Convert graph to serializable format
        # Nodes: list of {id, type, area, geometry_wkt}
        # Edges: list of {source, target, type}
        graph_data = {
            'nodes': [],
            'edges': []
        }

        for n, data in G.nodes(data=True):
            node_info = {
                'id': n,
                'type': data.get('type'),
                'area': data.get('area'),
                # We won't export geometry for graph nodes in JSON to save space/complexity unless needed,
                # but for verification it might be useful.
                # 'geometry': wkt_dumps(data.get('geometry')) if 'geometry' in data else None
            }
            graph_data['nodes'].append(node_info)

        for u, v, data in G.edges(data=True):
            edge_info = {
                'source': u,
                'target': v,
                'type': data.get('type')
            }
            graph_data['edges'].append(edge_info)

        # Sort for deterministic output
        graph_data['nodes'].sort(key=lambda x: x['id'])
        graph_data['edges'].sort(key=lambda x: (x['source'], x['target']))

        # Render Reference Image
        fig, ax = plt.subplots(figsize=(8, 8))
        plot_plan(plan, ax=ax, title=f"Plan {idx}")
        # Save
        img_path = os.path.join(IMG_DIR, f'plan_{idx}.png')
        plt.savefig(img_path, dpi=100)
        plt.close(fig)

        # Prepare data for JSON export
        # We need to export the geometries of the plan so .NET can load them.
        # We will use WKT.
        plan_export = {
            'id': idx,
            'geometries': {},
            'reference_graph': graph_data
        }

        # Categories usually plotted
        categories = ["living","bedroom","bathroom","kitchen","door","window","wall","front_door","balcony"]

        all_geoms = []

        for cat in categories:
            if cat in plan:
                # plan[cat] can be a single geometry or list or Multi.
                # resplan_utils.get_geometries flattens it to a list of atomic geometries
                geoms = get_geometries(plan[cat])
                wkt_list = [wkt_dumps(g) for g in geoms]
                if wkt_list:
                    plan_export['geometries'][cat] = wkt_list
                    all_geoms.extend(geoms)

        # Calculate bounds for .NET scaling
        if all_geoms:
            minx = min(g.bounds[0] for g in all_geoms)
            miny = min(g.bounds[1] for g in all_geoms)
            maxx = max(g.bounds[2] for g in all_geoms)
            maxy = max(g.bounds[3] for g in all_geoms)
            plan_export['bounds'] = [minx, miny, maxx, maxy]
        else:
            plan_export['bounds'] = [0,0,0,0]

        samples.append(plan_export)

    # Save JSON
    with open(OUTPUT_JSON, 'w') as f:
        json.dump(samples, f, indent=2)

    print(f"Exported {len(samples)} samples to {OUTPUT_JSON}")

if __name__ == '__main__':
    main()
