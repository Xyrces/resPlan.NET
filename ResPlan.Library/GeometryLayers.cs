using NetTopologySuite.Geometries;

namespace ResPlan.Library;

/// <summary>Shared footprint policy for view bounds and containing-polygon constraints.</summary>
internal static class GeometryLayers
{
    private static readonly string[] FootprintCategories =
    [
        "living", "bedroom", "bathroom", "kitchen", "door", "window", "wall", "front_door", "balcony",
        "stairs", "stair", "elevator", "foyer", "corridor", "entrance"
    ];

    internal static List<Geometry> GetFootprint(Dictionary<string, List<Geometry>> categories)
    {
        var visible = new List<Geometry>();
        foreach (var category in FootprintCategories)
            if (categories.TryGetValue(category, out var geometries))
                visible.AddRange(GeometryParts.Enumerate(geometries));

        // Auxiliary plots and neighboring properties are not part of a floorplan's footprint.
        // For custom datasets without visible/anchor geometry, use whatever geometry is present.
        if (visible.Count == 0)
            foreach (var geometries in categories.Values)
                visible.AddRange(GeometryParts.Enumerate(geometries));

        // Native imports may expose the same list through both `stair` and `stairs`.
        return visible.Distinct<Geometry>(ReferenceEqualityComparer.Instance).ToList();
    }

    internal static Envelope GetBounds(IEnumerable<Geometry> geometries)
    {
        var bounds = new Envelope();
        foreach (var geometry in geometries) bounds.ExpandToInclude(geometry.EnvelopeInternal);
        return bounds;
    }
}
