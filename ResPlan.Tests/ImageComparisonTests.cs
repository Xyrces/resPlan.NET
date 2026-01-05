using System;
using System.IO;
using System.Linq;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;
using ResPlan.Library;

namespace ResPlan.Tests
{
    public class ImageComparisonTests
    {
        private readonly ITestOutputHelper _output;

        public ImageComparisonTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void VerifyImageGenerationAgainstReference()
        {
            string jsonPath = "resplan_samples.json";
            var plans = PlanLoader.LoadPlans(jsonPath);
            Assert.NotEmpty(plans);

            foreach (var plan in plans)
            {
                // Generate C# Image
                string actualPath = $"actual_plan_{plan.Id}.png";
                PlanRenderer.Render(plan, actualPath);

                // Load Reference Image
                string refPath = Path.Combine("ReferenceImages", $"plan_{plan.Id}.png");
                Assert.True(File.Exists(refPath), $"Reference image {refPath} not found.");

                // Compare
                CompareImages(refPath, actualPath, plan.Id);
            }
        }

        private void CompareImages(string refPath, string actualPath, int planId)
        {
            using var refImg = SKBitmap.Decode(refPath);
            using var actImg = SKBitmap.Decode(actualPath);

            Assert.Equal(refImg.Width, actImg.Width);
            Assert.Equal(refImg.Height, actImg.Height);

            // Perceptual diff or RMSE.
            // Since tech stacks differ, we expect pixel differences.
            // Let's calculate Mean Squared Error (MSE) per channel.

            long diffSum = 0;
            long pixelCount = refImg.Width * refImg.Height;

            for (int y = 0; y < refImg.Height; y++)
            {
                for (int x = 0; x < refImg.Width; x++)
                {
                    var pRef = refImg.GetPixel(x, y);
                    var pAct = actImg.GetPixel(x, y);

                    diffSum += Sq(pRef.Red - pAct.Red) +
                               Sq(pRef.Green - pAct.Green) +
                               Sq(pRef.Blue - pAct.Blue);
                }
            }

            double mse = diffSum / (double)(pixelCount * 3);
            double rmse = Math.Sqrt(mse);

            _output.WriteLine($"Plan {planId} Comparison: RMSE = {rmse:F2}");

            // Define a tolerance. 0 is perfect.
            // Given font rendering, anti-aliasing differences, etc.
            // 255 is max difference.
            // Let's verify what the RMSE actually is first by failing if it's too high?
            // Or just printing it? Xunit doesn't print easily unless failing.

            // User requested "verify we have a perfect implementation".
            // But "using the same seed/data" implies deterministic output.
            // If the rendering logic is identical (color mapping, coordinate transform), it should be close.
            // But Python's matplotlib vs SkiaSharp will differ in anti-aliasing and exact line rasterization.
            // I'll set a reasonable threshold for regression testing.
            // If the user insists on "comparison to jupyter notebook output", and we are using a different library,
            // "visual equivalence" is the goal.

            // Let's set a tolerance.
            // If RMSE < 50 (on 0-255 scale), it's somewhat similar.
            // If RMSE < 10, it's very similar.
            // I suspect it will be high because of background colors, axes (matplotlib draws axes by default?), etc.
            // My renderer draws WHITE background. Matplotlib draws axes unless turned off.
            // In my python script I used `plot_plan(..., ax=ax)`.
            // `plot_plan` does `ax.set_axis_off()`.
            // So axes are off. Background should be white.

            // Let's assert RMSE < 20.
            Assert.True(rmse < 20, $"Image mismatch for {refPath}. RMSE: {rmse}");
        }

        private long Sq(int x) => x * x;
    }
}
