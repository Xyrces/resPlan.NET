import pickle
import os
import sys
import networkx as nx
from shapely.wkt import dumps as wkt_dumps

# Helper to ensure we can import resplan_utils if it's in the same dir or ResPlan dir
current_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(current_dir)
if os.path.exists(os.path.join(current_dir, 'ResPlan')):
    sys.path.append(os.path.join(current_dir, 'ResPlan'))

try:
    from resplan_utils import plan_to_graph, normalize_keys, get_geometries
except ImportError as e:
    # Fallback/Debug info
    print(f"Warning: Could not import resplan_utils: {e}", file=sys.stderr)
    def normalize_keys(plan): pass
    def get_geometries(obj): return []
    def plan_to_graph(plan): return nx.Graph()

def shapely_to_wkt_list(geoms):
    if geoms is None:
        return []
    if not isinstance(geoms, list):
        geoms = [geoms]
    return [wkt_dumps(g) for g in geoms]

def load_resplan_data(pkl_path, max_items=None):
    """
    Loads ResPlan.pkl and converts it to a list of dicts suitable for .NET consumption.
    """
    if not os.path.exists(pkl_path):
        raise FileNotFoundError(f"{pkl_path} not found")

    with open(pkl_path, 'rb') as f:
        plans = pickle.load(f)

    export_data = []

    count = 0
    for idx, plan in enumerate(plans):
        if max_items is not None and count >= max_items:
            break

        normalize_keys(plan)

        # Structure matching PlanLoader expectations (using legacy JSON names)
        plan_export = {
            'id': idx,
            'geometries': {},
            'reference_graph': None,
            'bounds': [0.0, 0.0, 0.0, 0.0]
        }

        categories = ["living","bedroom","bathroom","kitchen","door","window","wall","front_door","balcony"]
        all_geoms = []

        for cat in categories:
            if cat in plan:
                geoms = get_geometries(plan[cat])
                if geoms:
                    wkt_list = [wkt_dumps(g) for g in geoms]
                    plan_export['geometries'][cat] = wkt_list
                    all_geoms.extend(geoms)

        if all_geoms:
            minx = min(g.bounds[0] for g in all_geoms)
            miny = min(g.bounds[1] for g in all_geoms)
            maxx = max(g.bounds[2] for g in all_geoms)
            maxy = max(g.bounds[3] for g in all_geoms)
            plan_export['bounds'] = [float(minx), float(miny), float(maxx), float(maxy)]

        # Graph
        G = plan_to_graph(plan)
        graph_data = {'nodes': [], 'edges': []}

        for n, data in G.nodes(data=True):
            # Node IDs might be strings like 'living_0'.
            # We should keep them as strings to match .NET string IDs.
            node_info = {
                'id': str(n),
                'type': str(data.get('type', '')),
                'area': float(data.get('area', 0.0)),
            }
            graph_data['nodes'].append(node_info)

        for u, v, data in G.edges(data=True):
            edge_info = {
                'source': str(u),
                'target': str(v),
                'type': str(data.get('type', ''))
            }
            graph_data['edges'].append(edge_info)

        plan_export['reference_graph'] = graph_data

        export_data.append(plan_export)
        count += 1

    return export_data
