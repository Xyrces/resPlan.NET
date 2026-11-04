using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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

        public static void Render(Plan plan, string outputPath, int width = 800, int height = 800)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            var bounds = plan.Bounds;
            if (bounds == null || bounds.IsNull) return;

            // Fit the actual bounds into the requested canvas, preserving aspect ratio.
            double w = bounds.Width;
            double h = bounds.Height;
            double xScale = w > 0 ? width / w : double.PositiveInfinity;
            double yScale = h > 0 ? height / h : double.PositiveInfinity;
            double scale = w == 0 && h == 0 ? 1 : Math.Min(xScale, yScale);
            double offsetX = (width - w * scale) / 2;
            double offsetY = (height - h * scale) / 2;

            SKPoint Transform(double x, double y)
            {
                return new SKPoint(
                    (float)(offsetX + (x - bounds.MinX) * scale),
                    (float)(offsetY + (bounds.MaxY - y) * scale)
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

                foreach (var geom in GeometryParts.Enumerate(plan.Geometries[cat]))
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
