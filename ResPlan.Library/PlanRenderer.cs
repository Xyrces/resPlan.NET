using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace ResPlan.Library
{
    public class PlanRenderer
    {
        private static readonly Dictionary<string, SKColor> CategoryColors = new Dictionary<string, SKColor>
        {
            { "living", SKColor.Parse("#d9d9d9") },    // light gray
            { "bedroom", SKColor.Parse("#66c2a5") },    // greenish
            { "bathroom", SKColor.Parse("#fc8d62") },   // orange
            { "kitchen", SKColor.Parse("#8da0cb") },    // blue
            { "door", SKColor.Parse("#e78ac3") },       // pink
            { "window", SKColor.Parse("#a6d854") },     // lime
            { "wall", SKColor.Parse("#ffd92f") },       // yellow
            { "front_door", SKColor.Parse("#a63603") }, // dark reddish-brown
            { "balcony", SKColor.Parse("#b3b3b3") },    // dark gray
            { "stairs", SKColor.Parse("#00008B") }      // DarkBlue
        };

        // Matches resplan_utils.py order
        private static readonly List<string> DrawOrder = new List<string>
        {
            "living", "bedroom", "bathroom", "kitchen", "door", "window", "wall", "front_door", "balcony", "stairs"
        };

        public static void RenderFloor(BuildingFloor floor, string outputPath, int width = 800, int height = 800)
        {
             // Create a composite plan for rendering
             // We can shallow copy the plan logic
             // But Render takes a Plan.
             // We can create a temporary Plan object that merges Geometries + AdditionalGeometries

             // Create a composite plan for rendering
             // We use deep copy of the lists to avoid mutating the original plan
             var compositePlan = new Plan
             {
                 Id = floor.Plan.Id,
                 Geometries = new Dictionary<string, List<Geometry>>(),
                 Bounds = new Envelope(floor.Plan.Bounds)
             };

             foreach(var kvp in floor.Plan.Geometries)
             {
                 compositePlan.Geometries[kvp.Key] = new List<Geometry>(kvp.Value);
             }

             foreach(var kvp in floor.AdditionalGeometries)
             {
                 if(compositePlan.Geometries.ContainsKey(kvp.Key))
                 {
                     compositePlan.Geometries[kvp.Key].AddRange(kvp.Value);
                 }
                 else
                 {
                     compositePlan.Geometries[kvp.Key] = new List<Geometry>(kvp.Value);
                 }

                 // Expand bounds
                 foreach(var g in kvp.Value)
                 {
                     compositePlan.Bounds.ExpandToInclude(g.EnvelopeInternal);
                 }
             }

             Render(compositePlan, outputPath, width, height);
        }

        public static void Render(Plan plan, string outputPath, int width = 800, int height = 800)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            var bounds = plan.Bounds;
            if (bounds == null || bounds.IsNull) return;

            // Calculate fit to square logic
            double w = bounds.Width;
            double h = bounds.Height;

            double minX = bounds.MinX;
            double minY = bounds.MinY;
            double maxX = bounds.MaxX;
            double maxY = bounds.MaxY;

            if (w > h)
            {
                // wider, expand height
                double cy = (minY + maxY) / 2.0;
                double half = w / 2.0;
                minY = cy - half;
                maxY = cy + half;
            }
            else
            {
                // taller, expand width
                double cx = (minX + maxX) / 2.0;
                double half = h / 2.0;
                minX = cx - half;
                maxX = cx + half;
            }

            float scale = (float)(width / (maxX - minX));

            SKPoint Transform(double x, double y)
            {
                return new SKPoint(
                    (float)((x - minX) * scale),
                    height - (float)((y - minY) * scale)
                );
            }

            // Draw Geometries
            foreach (var cat in DrawOrder)
            {
                if (!plan.Geometries.ContainsKey(cat)) continue;

                var color = CategoryColors.ContainsKey(cat) ? CategoryColors[cat] : SKColors.Black;
                using var paint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };

                using var strokePaint = new SKPaint
                {
                    Color = SKColors.Black,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f,
                    IsAntialias = true
                };

                foreach (var geom in plan.Geometries[cat])
                {
                    if (geom is Polygon poly)
                    {
                        using var path = new SKPath();
                        var coords = poly.ExteriorRing.Coordinates;
                        if (coords.Length > 0)
                        {
                            var p0 = Transform(coords[0].X, coords[0].Y);
                            path.MoveTo(p0);
                            for (int i = 1; i < coords.Length; i++)
                            {
                                path.LineTo(Transform(coords[i].X, coords[i].Y));
                            }
                            path.Close();
                        }

                        // Holes
                        for (int i = 0; i < poly.NumInteriorRings; i++)
                        {
                             var hole = poly.GetInteriorRingN(i).Coordinates;
                             if (hole.Length > 0)
                             {
                                 var p0 = Transform(hole[0].X, hole[0].Y);
                                 path.MoveTo(p0);
                                 for (int j = 1; j < hole.Length; j++)
                                 {
                                     path.LineTo(Transform(hole[j].X, hole[j].Y));
                                 }
                                 path.Close();
                             }
                        }
                        path.FillType = SKPathFillType.EvenOdd;

                        canvas.DrawPath(path, paint);
                        canvas.DrawPath(path, strokePaint);
                    }
                    else if (geom is LineString ls)
                    {
                         using var path = new SKPath();
                         var coords = ls.Coordinates;
                         if (coords.Length > 0)
                         {
                             var p0 = Transform(coords[0].X, coords[0].Y);
                             path.MoveTo(p0);
                             for (int i = 1; i < coords.Length; i++)
                             {
                                 path.LineTo(Transform(coords[i].X, coords[i].Y));
                             }
                         }

                         using var linePaint = new SKPaint
                         {
                             Color = color,
                             Style = SKPaintStyle.Stroke,
                             StrokeWidth = 0.5f,
                             IsAntialias = true
                         };
                         canvas.DrawPath(path, linePaint);
                    }
                }
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(outputPath);
            data.SaveTo(stream);
        }
    }
}
