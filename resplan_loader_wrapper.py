import sys
import json
import os

# Add directory of this script to path to find resplan_loader
current_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(current_dir)

try:
    import resplan_loader
except ImportError:
    # Try finding it in parent or current
    pass

def main():
    if len(sys.argv) < 2:
        sys.exit(1)

    pkl_path = sys.argv[1]
    limit = None
    if len(sys.argv) > 2:
        try:
            limit = int(sys.argv[2])
        except ValueError:
            pass

    try:
        data = resplan_loader.load_resplan_data(pkl_path, max_items=limit)
        # Convert data to be JSON serializable if needed
        # load_resplan_data returns dicts with primitive types (str, float, int) which is JSON ready
        print(json.dumps(data))
    except Exception as e:
        sys.stderr.write(str(e))
        sys.exit(1)

if __name__ == '__main__':
    main()
