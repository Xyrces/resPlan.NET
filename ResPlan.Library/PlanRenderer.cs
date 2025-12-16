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
            { "balcony", SKColor.Parse("#b3b3b3") }     // dark gray
        };

        private static readonly List<string> DrawOrder = new List<string>
        {
            "living", "bedroom", "bathroom", "kitchen", "balcony", "wall", "door", "window", "front_door"
        };

        public static void Render(Plan plan, string outputPath, int width = 800, int height = 800)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            var bounds = plan.Bounds;
            if (bounds == null || bounds.IsNull) return;

            // Calculate fit to square logic matching Python script
            // Python script expands bounds to be square.
            // Width/Height
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

            // Now map [minX, maxX] -> [0, width]
            // And [minY, maxY] -> [height, 0] (Inverted Y)

            float scale = (float)(width / (maxX - minX)); // Since it's square, scaleX == scaleY

            SKPoint Transform(double x, double y)
            {
                // x' = (x - minX) * scale
                // y' = height - (y - minY) * scale
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
                    StrokeWidth = 1, // Matplotlib linewidth=0.5. At scale? No, 0.5 points.
                    // This is hard to match exactly. 1px is often close.
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
                             StrokeWidth = 2,
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
