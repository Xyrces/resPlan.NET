using System;
using System.Collections.Generic;
using System.IO;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Python.Runtime;

namespace ResPlan.Library
{
    public class PickleLoader
    {
        private static bool _initialized = false;
        private static readonly WKTReader _wktReader = new WKTReader();

        public static void Initialize(string pythonDllPath = null)
        {
            if (_initialized) return;

            if (!string.IsNullOrEmpty(pythonDllPath))
            {
                Runtime.PythonDLL = pythonDllPath;
            }
            PythonEngine.Initialize();
            _initialized = true;
        }

        public static List<Plan> LoadFromPickle(string pklPath)
        {
            if (!_initialized)
            {
                Initialize();
            }

            var plans = new List<Plan>();

            using (Py.GIL())
            {
                dynamic pickle = Py.Import("pickle");
                dynamic builtins = Py.Import("builtins");
                dynamic shapelyWkt = Py.Import("shapely.wkt");

                using (dynamic f = builtins.open(pklPath, "rb"))
                {
                    dynamic loadedPlans = pickle.load(f);

                    int idx = 0;
                    foreach (dynamic pyPlan in loadedPlans)
                    {
                        var plan = ParsePyPlan(pyPlan, idx, shapelyWkt, builtins);
                        plans.Add(plan);
                        idx++;
                    }
                }
            }

            return plans;
        }

        private static Plan ParsePyPlan(dynamic pyPlan, int index, dynamic shapelyWkt, dynamic builtins)
        {
            var plan = new Plan
            {
                Id = index,
                Geometries = new Dictionary<string, List<Geometry>>()
            };

            var pyKeys = pyPlan.keys();
            var categories = new HashSet<string> { "living", "bedroom", "bathroom", "kitchen", "door", "window", "wall", "front_door", "balcony" };
            var allGeoms = new List<Geometry>();

            foreach (dynamic keyObj in pyKeys)
            {
                string key = (string)keyObj;
                if (categories.Contains(key))
                {
                    dynamic val = pyPlan[key];
                    if (val == null) continue;

                    var geomList = new List<Geometry>();
                    bool isList = (bool)builtins.isinstance(val, builtins.list);

                    if (isList)
                    {
                        foreach (dynamic g in val)
                        {
                            AddGeometry(g, geomList, shapelyWkt);
                        }
                    }
                    else
                    {
                        AddGeometry(val, geomList, shapelyWkt);
                    }

                    plan.Geometries[key] = geomList;
                    allGeoms.AddRange(geomList);
                }
            }

            if (allGeoms.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach(var g in allGeoms)
                {
                    var env = g.EnvelopeInternal;
                    if (env.MinX < minX) minX = env.MinX;
                    if (env.MinY < minY) minY = env.MinY;
                    if (env.MaxX > maxX) maxX = env.MaxX;
                    if (env.MaxY > maxY) maxY = env.MaxY;
                }
                plan.Bounds = new Envelope(minX, maxX, minY, maxY);
            }
            else
            {
                plan.Bounds = new Envelope(0,0,0,0);
            }

            return plan;
        }

        private static void AddGeometry(dynamic pyGeom, List<Geometry> list, dynamic shapelyWkt)
        {
            if (pyGeom == null) return;
            try {
                if ((bool)pyGeom.is_empty) return;
            } catch { return; }

            string wkt = (string)shapelyWkt.dumps(pyGeom);
            var geom = _wktReader.Read(wkt);

            if (geom is GeometryCollection gc && !(geom is MultiPolygon) && !(geom is MultiLineString))
            {
                for(int i=0; i<gc.NumGeometries; i++)
                {
                    list.Add(gc.GetGeometryN(i));
                }
            }
            else
            {
                if (geom is MultiPolygon mp)
                {
                    for(int i=0; i<mp.NumGeometries; i++) list.Add(mp.GetGeometryN(i));
                }
                else if (geom is MultiLineString ml)
                {
                    for(int i=0; i<ml.NumGeometries; i++) list.Add(ml.GetGeometryN(i));
                }
                else
                {
                    list.Add(geom);
                }
            }
        }
    }
}
