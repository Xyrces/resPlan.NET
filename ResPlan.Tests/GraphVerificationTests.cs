using System.IO;
using System.Linq;
using Xunit;
using ResPlan.Library;

namespace ResPlan.Tests
{
    public class GraphVerificationTests
    {
        [Fact]
        public void VerifyGraphGenerationAgainstReference()
        {
            string jsonPath = "resplan_samples.json";
            Assert.True(File.Exists(jsonPath), $"Test data {jsonPath} not found.");

            var plans = PlanLoader.LoadPlans(jsonPath);
            Assert.NotEmpty(plans);

            foreach (var plan in plans)
            {
                // Ensure we have a reference graph
                Assert.NotNull(plan.ReferenceGraph);

                // Generate graph
                var graph = GraphGenerator.GenerateGraph(plan);

                // Verify Nodes Count
                Assert.Equal(plan.ReferenceGraph.Nodes.Count, graph.Nodes.Count);

                // Verify Edges Count
                Assert.Equal(plan.ReferenceGraph.Edges.Count, graph.Edges.Count);

                // Verify specific nodes exist
                foreach(var kvp in plan.ReferenceGraph.Nodes)
                {
                    var nodeId = kvp.Key;
                    var referenceNode = kvp.Value;

                    Assert.True(graph.Nodes.ContainsKey(nodeId), $"Node {nodeId} missing in generated graph for plan {plan.Id}");
                    Assert.Equal(referenceNode.Type, graph.Nodes[nodeId].Type);
                }

                // Verify edges exist (undirected)
                foreach(var referenceEdge in plan.ReferenceGraph.Edges)
                {
                    bool exists = graph.Edges.Any(e =>
                        (e.SourceId == referenceEdge.SourceId && e.TargetId == referenceEdge.TargetId && e.Type == referenceEdge.Type) ||
                        (e.SourceId == referenceEdge.TargetId && e.TargetId == referenceEdge.SourceId && e.Type == referenceEdge.Type));

                    Assert.True(exists, $"Edge {referenceEdge.SourceId}-{referenceEdge.TargetId} ({referenceEdge.Type}) missing in generated graph for plan {plan.Id}");
                }
            }
        }
    }
}
