using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

namespace ResPlan.Library
{
    /// <summary>
    /// Generates multi-story buildings by stacking compatible floor plans.
    /// </summary>
    public class BuildingGenerator
    {
        private readonly GeometryFactory _geometryFactory = new GeometryFactory();

        /// <summary>
        /// Generates a building with a specified number of floors using the provided plans.
        /// </summary>
        /// <param name="availablePlans">A list of candidate plans to stack.</param>
        /// <param name="targetFloors">The desired number of floors.</param>
        /// <returns>A <see cref="Building"/> object containing the stacked floors.</returns>
        public Building GenerateBuilding(List<Plan> availablePlans, int targetFloors)
        {
            var building = new Building();
            if (availablePlans == null || availablePlans.Count == 0)
                return building;

            // Work on copies to avoid mutating the input list objects
            var unusedPlans = availablePlans.Select(ClonePlan).ToList();

            // Step 1: Normalize all plans (Center Front Door to 0,0)
            foreach (var p in unusedPlans)
            {
                NormalizePlan(p);
            }

            // Step 2: Sort by Area descending (simple heuristic)
            unusedPlans = unusedPlans.OrderByDescending(p => p.Bounds.Area).ToList();

            if (unusedPlans.Count == 0) return building;

            // Step 3: Pick Base Floor
            var basePlan = unusedPlans[0];
            unusedPlans.RemoveAt(0);

            var floor0 = new BuildingFloor
            {
                FloorNumber = 0,
                Plan = basePlan
            };
            AddStairCore(floor0);
            building.Floors.Add(floor0);

            // Step 4: Stack subsequent floors
            var currentFloorPlan = basePlan;

            for (int i = 1; i < targetFloors; i++)
            {
                Plan bestFitPlan = null;
                int bestFitIndex = -1;
                double bestFitScore = -1;

                // Search for a plan that fits
                for (int j = 0; j < unusedPlans.Count; j++)
                {
                    var candidate = unusedPlans[j];

                    // We need to test rotations WITHOUT mutating 'candidate' permanently for the next iteration of 'j' loop
                    // But 'candidate' is already a clone from the input.
                    // However, if we rotate it for test 1, we must rotate it back or use a temp copy.
                    // Since geometry operations can be expensive, let's clone for the inner loop?
                    // Or just rotate, check, rotate back? Rotate back is cheaper.

                    for (int r = 0; r < 4; r++)
                    {
                        // Rotate 90 degrees (relative to 0,0)
                        if (r > 0)
                        {
                            candidate.Rotate(Math.PI / 2.0, new Coordinate(0, 0));
                        }

                        // Check fit
                        var currentGeom = _geometryFactory.ToGeometry(currentFloorPlan.Bounds);
                        var candidateGeom = _geometryFactory.ToGeometry(candidate.Bounds);

                        var intersection = currentGeom.Intersection(candidateGeom);
                        var coverage = intersection.Area / candidateGeom.Area;

                        if (coverage > 0.95) // 95% contained
                        {
                            if (candidateGeom.Area > bestFitScore)
                            {
                                bestFitScore = candidateGeom.Area;
                                // We found a better fit. Snapshot the plan in its current state.
                                bestFitPlan = ClonePlan(candidate);
                                bestFitIndex = j;
                            }
                        }
                    }

                    // Rotate back to original state for next outer loop usage
                    candidate.Rotate(Math.PI / 2.0, new Coordinate(0, 0));
                }

                if (bestFitPlan != null)
                {
                    unusedPlans.RemoveAt(bestFitIndex);
                    var nextFloor = new BuildingFloor
                    {
                        FloorNumber = i,
                        Plan = bestFitPlan
                    };
                    AddStairCore(nextFloor);
                    building.Floors.Add(nextFloor);
                    currentFloorPlan = bestFitPlan;
                }
                else
                {
                    break;
                }
            }

            return building;
        }

        private Plan ClonePlan(Plan source)
        {
            var newPlan = new Plan
            {
                Id = source.Id,
                Geometries = new Dictionary<string, List<Geometry>>(),
                // Bounds will be recalculated or copied
                ReferenceGraph = null // Not deep cloning graph for now as we don't use it for generation
            };

            if (source.Bounds != null)
            {
                newPlan.Bounds = new Envelope(source.Bounds);
            }

            foreach(var kvp in source.Geometries)
            {
                newPlan.Geometries[kvp.Key] = new List<Geometry>();
                foreach(var g in kvp.Value)
                {
                    newPlan.Geometries[kvp.Key].Add(g.Copy());
                }
            }

            return newPlan;
        }

        private void NormalizePlan(Plan plan)
        {
            if (plan.Geometries.ContainsKey("front_door") && plan.Geometries["front_door"].Count > 0)
            {
                var fd = plan.Geometries["front_door"][0];
                var center = fd.Centroid.Coordinate;

                plan.Translate(-center.X, -center.Y);
            }
        }

        private void AddStairCore(BuildingFloor floor)
        {
            // Create a stair core rectangle 4x3m placed "south" of 0,0
            var coords = new Coordinate[]
            {
                new Coordinate(-2, -4),
                new Coordinate(2, -4),
                new Coordinate(2, 0), // Touches 0,0 (Front Door)
                new Coordinate(-2, 0),
                new Coordinate(-2, -4)
            };
            var shell = _geometryFactory.CreateLinearRing(coords);
            var poly = _geometryFactory.CreatePolygon(shell);

            floor.AdditionalGeometries["stairs"] = new List<Geometry> { poly };
        }
    }
}
