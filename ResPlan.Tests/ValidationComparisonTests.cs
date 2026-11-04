using System.Text.Json;
using Xunit;

namespace ResPlan.Tests;

public class ValidationComparisonTests
{
    [Theory]
    [InlineData("1e-100", "2e-100")]
    [InlineData("1e-100", "0")]
    [InlineData("5e-324", "0")]
    [InlineData("5e-324", "1e-323")]
    [InlineData("1.2345678901234567e-20", "1.2345678901234568e-20")]
    [InlineData("18446744073709551615", "18446744073709551614")]
    [InlineData("79228162514264337593543950336", "79228162514264337593543950337")]
    [InlineData("1", "1.00000000000000001")]
    [InlineData("1e400", "2e400")]
    public void ExactNumbersDoNotCollapseThroughDecimalOrDoubleRounding(string expected, string actual)
    {
        var errors = Compare(expected, actual);

        Assert.Equal(1, errors.Count);
        Assert.Equal(1, errors.CountByStage["oracle"]);
    }

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("1e-100", "0.1e-99")]
    [InlineData("1000", "1E+3")]
    [InlineData("0.000001", "1e-6")]
    [InlineData("18446744073709551615", "184467440737095516150e-1")]
    [InlineData("-0.0", "0")]
    public void EquivalentNumericNotationStillMatches(string expected, string actual)
    {
        Assert.Equal(0, Compare(expected, actual).Count);
    }

    [Theory]
    [InlineData("""{"metadata":{"area":$}}""")]
    [InlineData("""{"metadata":{"values":[$]}}""")]
    [InlineData("""{"metadata":{"reference_graph":{"nodes":{"room":{"area":$}}}}}""")]
    [InlineData("""{"bounds":[0,0,$,1]}""")]
    [InlineData("""{"wall_width":$}""")]
    [InlineData("""{"reference_graph":{"metadata":{"area":$}}}""")]
    [InlineData("""{"source_graph":{"metadata":{"area":$}}}""")]
    [InlineData("""{"reference_graph":{"nodes":{"room":{"metadata":{"area":$}}}}}""")]
    [InlineData("""{"source_graph":{"nodes":{"room":{"metadata":{"area":$}}}}}""")]
    [InlineData("""{"reference_graph":{"nodes":{"room":{"weight":$}}}}""")]
    [InlineData("""{"reference_graph":{"edges":[{"metadata":{"area":$}}]}}""")]
    [InlineData("""{"source_graph":{"edges":[{"metadata":{"area":$}}]}}""")]
    public void AreaToleranceDoesNotLeakIntoOtherPlanValues(string template)
    {
        var errors = Compare(template.Replace("$", "1"), template.Replace("$", "1.000000000001"),
            areaAbsolute: 1e-10, areaRelative: 1e-12);

        Assert.Equal(1, errors.Count);
        Assert.Equal(1, errors.CountByStage["oracle"]);
    }

    [Theory]
    [InlineData("1", "1.00000000005", 1e-10, 0, 0)]
    [InlineData("1000", "1000.0000000005", 0, 1e-12, 0)]
    [InlineData("1", "1.000000001", 1e-10, 0, 1)]
    [InlineData("1000", "1000.000000002", 0, 1e-12, 1)]
    [InlineData("1e400", "2e400", 1e-10, 1e-12, 1)]
    public void OnlyGraphNodeAreasUsePositiveConfiguredTolerance(
        string expected, string actual, double areaAbsolute, double areaRelative, long errorCount)
    {
        foreach (var graph in new[] { "reference_graph", "source_graph" })
        {
            var errors = Compare(NodeArea(graph, expected), NodeArea(graph, actual),
                areaAbsolute: areaAbsolute, areaRelative: areaRelative);

            Assert.Equal(errorCount, errors.Count);
        }
    }

    [Theory]
    [InlineData("1e-100", "2e-100", 1)]
    [InlineData("1", "1.00000000000000001", 1)]
    [InlineData("18446744073709551615", "18446744073709551614", 1)]
    [InlineData("1e-100", "0.1e-99", 0)]
    public void MessagePackZeroToleranceChecksNodeAreasExactly(string expected, string actual, long errorCount)
    {
        foreach (var graph in new[] { "reference_graph", "source_graph" })
        {
            var errors = Compare(NodeArea(graph, expected), NodeArea(graph, actual), stage: "messagepack");

            Assert.Equal(errorCount, errors.Count);
            if (errorCount != 0) Assert.Equal(errorCount, errors.CountByStage["messagepack"]);
        }
    }

    private static string NodeArea(string graph, string number)
        => "{\"" + graph + "\":{\"nodes\":{\"room\":{\"area\":" + number + "}}}}";

    private static Validation.ComparisonErrors Compare(string expected, string actual,
        string stage = "oracle", double areaAbsolute = 0, double areaRelative = 0)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        var errors = new Validation.ComparisonErrors();
        Validation.Compare(expectedDocument.RootElement, actualDocument.RootElement,
            "plans[0]", errors, stage, areaAbsolute, areaRelative);
        return errors;
    }
}
