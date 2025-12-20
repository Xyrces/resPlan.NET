using System;
using System.Numerics;
using NetTopologySuite.Geometries;

namespace ResPlan.Library
{
    public class PlanGenerationConstraints
    {
        public Polygon BoundingPolygon { get; set; }
        public Vector2? FrontDoorFacing { get; set; }
        public Vector2? GarageFacing { get; set; }
    }
}
