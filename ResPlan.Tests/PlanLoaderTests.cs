using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ResPlan.Library;

namespace ResPlan.Tests
{
    public class PlanLoaderTests
    {
        [Fact]
        public async Task LoadPlansAsync_WithSampleData_LoadsCorrectly()
        {
            // Arrange
            var samplePath = Path.Combine(Directory.GetCurrentDirectory(), "sample.pkl");
            // If running via 'dotnet test', the file needs to be copied to output.
            // But we created it in ResPlan.Tests source dir.
            // Let's ensure we find it.

            // NOTE: In dotnet test, working directory is usually bin/Debug/netX.X/
            // We should ensure sample.pkl is copied there.
            // Or we can construct path relative to known location.

            // Assuming sample.pkl is copied to output directory (we will add it to csproj).
            Assert.True(File.Exists("sample.pkl"), "sample.pkl must exist in test execution directory");

            // Act
            var plans = await PlanLoader.LoadPlansAsync(pklPathOverride: "sample.pkl");

            // Assert
            Assert.NotNull(plans);
            Assert.Equal(2, plans.Count);

            var plan0 = plans[0];
            Assert.Equal(0, plan0.Id);
            Assert.NotNull(plan0.Geometries);
            Assert.NotEmpty(plan0.Geometries);
            Assert.NotNull(plan0.ReferenceGraph);
            Assert.NotEmpty(plan0.ReferenceGraph.Nodes);
            Assert.NotEmpty(plan0.ReferenceGraph.Edges);

            var plan1 = plans[1];
            Assert.Equal(1, plan1.Id);
        }
    }
}
