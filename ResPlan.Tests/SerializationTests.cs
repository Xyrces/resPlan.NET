using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using ResPlan.Library;

namespace ResPlan.Tests
{
    public class SerializationReproTests
    {
        [Fact]
        public void LoadPlans_WithNaNArea_SanitizesData_And_SerializationSucceeds()
        {
            // Arrange
            var jsonContent = @"[
              {
                ""Id"": 1,
                ""Geometries"": {},
                ""Bounds"": [0, 0, 10, 10],
                ""reference_graph"": {
                  ""Nodes"": [
                    {
                      ""Id"": ""n1"",
                      ""Type"": ""t1"",
                      ""Area"": ""NaN""
                    },
                    {
                      ""Id"": ""n2"",
                      ""Type"": ""t2"",
                      ""Area"": ""Infinity""
                    }
                  ],
                  ""Edges"": []
                }
              }
            ]";

            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, jsonContent);

            try
            {
                // Act
                var plans = PlanLoader.LoadPlans(tempFile);

                // Assert
                Assert.NotNull(plans);
                Assert.Single(plans);
                var plan = plans[0];
                var n1 = plan.ReferenceGraph.Nodes["n1"];
                var n2 = plan.ReferenceGraph.Nodes["n2"];

                // Check values directly
                Assert.False(double.IsNaN(n1.Area), "Node Area should not be NaN");
                Assert.False(double.IsInfinity(n2.Area), "Node Area should not be Infinity");

                // Debugging: Identify what fails to serialize
                try
                {
                    JsonSerializer.Serialize(n1);
                }
                catch (Exception ex)
                {
                    throw new Exception("n1 serialization failed", ex);
                }

                try
                {
                    // This should use the [JsonConverter] on Plan.Bounds property
                    JsonSerializer.Serialize(plan);
                }
                catch (Exception ex)
                {
                     throw new Exception("Plan serialization failed", ex);
                }
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
