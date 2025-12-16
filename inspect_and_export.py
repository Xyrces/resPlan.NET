import pickle
import json
import os
import sys
import networkx as nx
import matplotlib.pyplot as plt
from shapely.geometry import mapping
from shapely.wkt import dumps as wkt_dumps
import numpy as np

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
    indices = [0, 10, 50]

    for idx in indices:
        if idx >= len(plans): continue
        print(f"Processing plan {idx}...")
        plan = plans[idx]
        normalize_keys(plan)

        categories = ["living","bedroom","bathroom","kitchen","door","window","wall","front_door","balcony"]
        all_geoms = []
        plan_export = {
            'id': idx,
            'geometries': {},
            'reference_graph': None,
            'bounds': [0,0,0,0]
        }

        for cat in categories:
            if cat in plan:
                geoms = get_geometries(plan[cat])
                wkt_list = [wkt_dumps(g) for g in geoms]
                if wkt_list:
                    plan_export['geometries'][cat] = wkt_list
                    all_geoms.extend(geoms)

        minx, miny, maxx, maxy = 0, 0, 1, 1
        if all_geoms:
            minx = min(g.bounds[0] for g in all_geoms)
            miny = min(g.bounds[1] for g in all_geoms)
            maxx = max(g.bounds[2] for g in all_geoms)
            maxy = max(g.bounds[3] for g in all_geoms)
            plan_export['bounds'] = [minx, miny, maxx, maxy]

        G = plan_to_graph(plan)
        graph_data = {'nodes': [], 'edges': []}

        for n, data in G.nodes(data=True):
            node_info = {
                'id': n,
                'type': data.get('type'),
                'area': data.get('area'),
            }
            graph_data['nodes'].append(node_info)

        for u, v, data in G.edges(data=True):
            edge_info = {
                'source': u,
                'target': v,
                'type': data.get('type')
            }
            graph_data['edges'].append(edge_info)

        graph_data['nodes'].sort(key=lambda x: x['id'])
        graph_data['edges'].sort(key=lambda x: (x['source'], x['target']))
        plan_export['reference_graph'] = graph_data

        fig = plt.figure(figsize=(8, 8), dpi=100)
        ax = fig.add_axes([0, 0, 1, 1])

        plot_plan(plan, ax=ax, title=None, legend=False, tight=False)

        w = maxx - minx
        h = maxy - miny

        if w > h:
            cy = (miny + maxy) / 2
            half = w / 2
            ax.set_ylim([cy - half, cy + half])
            ax.set_xlim([minx, maxx])
        else:
            cx = (minx + maxx) / 2
            half = h / 2
            ax.set_xlim([cx - half, cx + half])
            ax.set_ylim([miny, maxy])

        ax.set_axis_off()

        img_path = os.path.join(IMG_DIR, f'plan_{idx}.png')
        plt.savefig(img_path, dpi=100)
        plt.close(fig)

        samples.append(plan_export)

    with open(OUTPUT_JSON, 'w') as f:
        json.dump(samples, f, indent=2)

    print(f"Exported {len(samples)} samples to {OUTPUT_JSON}")

if __name__ == '__main__':
    main()
