# ResPlan.NET

ResPlan.NET is a .NET 9 library for working with the ResPlan dataset (Residential Floorplans). It provides functionality to load floorplan data (geometry and metadata), generate connectivity graphs (representing room adjacency and connections), and render floorplans to images.

This library is a .NET port/adaptation of the original Python implementation, designed to be performant and easy to use within the .NET ecosystem while leveraging the existing Python data ingestion logic.

## Prerequisites

*   **.NET 9 SDK**
*   **Python 3.x**: Required for the underlying data loader mechanism. The library manages a local virtual environment for dependencies.

## Installation

Add a reference to the `ResPlan.Library` project or install the NuGet package (if available).

## Quick Start

The following examples demonstrate the core functionality of the library.

### 1. Loading Plans

The `PlanLoader` class handles loading data. It uses a Python subprocess to parse the original `.pkl` data files securely.

```csharp
using ResPlan.Library;

// Load plans.
// Passing 'null' as the first argument triggers the default behavior:
// It ensures the data and Python dependencies are present, then loads the dataset.
// 'maxItems' is optional, used here to limit the load for demonstration.
var plans = await PlanLoader.LoadPlansAsync(null, maxItems: 10);

Console.WriteLine($"Loaded {plans.Count} plans.");
```

### 2. Generating Connectivity Graphs

Once a `Plan` is loaded, you can generate a graph representing the connectivity between rooms, doors, and windows using `GraphGenerator`.

```csharp
using ResPlan.Library;

foreach (var plan in plans)
{
    // Generate the graph based on geometry intersections and room types
    Graph graph = GraphGenerator.GenerateGraph(plan);

    Console.WriteLine($"Generated Graph for Plan {plan.Id}:");
    Console.WriteLine($"  Nodes: {graph.Nodes.Count}");
    Console.WriteLine($"  Edges: {graph.Edges.Count}");

    // Example: Inspecting nodes
    foreach(var node in graph.Nodes.Values)
    {
        Console.WriteLine($"    Node {node.Id} ({node.Type}) - Area: {node.Area:F2}");
    }
}
```

### 3. Rendering Floorplans

You can visualize the floorplan using `PlanRenderer`. This uses SkiaSharp to produce an image file.

```csharp
using ResPlan.Library;

foreach (var plan in plans)
{
    string outputPath = $"plan_{plan.Id}.png";

    // Render the plan to a PNG file (default 800x800)
    PlanRenderer.Render(plan, outputPath);

    Console.WriteLine($"Rendered plan to {outputPath}");
}
```

## Documentation

For a deep dive into the architecture, internal mechanisms (including the Python integration), and detailed API reference, please see [DOCUMENTATION.md](DOCUMENTATION.md).

## License

[License Information Here]
