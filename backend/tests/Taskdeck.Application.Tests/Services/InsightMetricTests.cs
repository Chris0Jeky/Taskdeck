using System.Reflection;
using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class InsightMetricTests
{
    // --- InsightMetric validation ---

    [Fact]
    public void InsightMetric_ShouldBeValid_WithCorrectValues()
    {
        var metric = new InsightMetric("proposal.acceptance_rate", 42, 7, "v2.1");
        metric.IsValid().Should().BeTrue();
    }

    [Fact]
    public void InsightMetric_ShouldBeInvalid_WithEmptyMetricName()
    {
        var metric = new InsightMetric("", 42, 7, "v2.1");
        metric.IsValid().Should().BeFalse();
    }

    [Fact]
    public void InsightMetric_ShouldBeInvalid_WithNegativeBucketedCount()
    {
        var metric = new InsightMetric("capture.count", -1, 7, "v2.1");
        metric.IsValid().Should().BeFalse();
    }

    [Fact]
    public void InsightMetric_ShouldBeInvalid_WithZeroTimePeriod()
    {
        var metric = new InsightMetric("capture.count", 10, 0, "v2.1");
        metric.IsValid().Should().BeFalse();
    }

    [Fact]
    public void InsightMetric_ShouldBeInvalid_WithEmptyPromptVersion()
    {
        var metric = new InsightMetric("capture.count", 10, 7, "");
        metric.IsValid().Should().BeFalse();
    }

    [Fact]
    public void InsightMetric_ShouldAccept_ZeroBucketedCount()
    {
        var metric = new InsightMetric("capture.count", 0, 30, "v1.0");
        metric.IsValid().Should().BeTrue();
    }

    // --- InsightCohort validation ---

    [Fact]
    public void InsightCohort_ShouldCalculateTotalCount()
    {
        var cohort = new InsightCohort(10, 5, 3);
        cohort.TotalCount.Should().Be(18);
    }

    [Fact]
    public void InsightCohort_ShouldCalculateAcceptanceRate()
    {
        var cohort = new InsightCohort(8, 2, 0);
        cohort.AcceptanceRate.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void InsightCohort_ShouldReturnZeroAcceptanceRate_WhenEmpty()
    {
        var cohort = new InsightCohort(0, 0, 0);
        cohort.AcceptanceRate.Should().Be(0.0);
    }

    [Fact]
    public void InsightCohort_ShouldReturnFullAcceptanceRate_WhenAllAccepted()
    {
        var cohort = new InsightCohort(100, 0, 0);
        cohort.AcceptanceRate.Should().Be(1.0);
    }

    // --- PII-freedom verification ---

    [Fact]
    public void InsightMetric_ShouldNotHaveStringFieldsThatCouldContainPii()
    {
        // The MetricName and PromptVersion fields are system-defined identifiers,
        // not user content. Verify there are no additional string properties
        // beyond these two known system fields.
        var stringProperties = typeof(InsightMetric)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToList();

        // Only MetricName and PromptVersion should be strings.
        // These are system-defined, not user-supplied.
        stringProperties.Should().BeEquivalentTo(["MetricName", "PromptVersion"],
            "InsightMetric must only have system-defined string fields, never user content");
    }

    [Fact]
    public void InsightCohort_ShouldHaveNoStringFields()
    {
        var stringProperties = typeof(InsightCohort)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .ToList();

        stringProperties.Should().BeEmpty(
            "InsightCohort must be entirely content-free with no string fields");
    }

    [Fact]
    public void InsightCohort_ShouldOnlyHaveNumericOrBoolFields()
    {
        var numericTypes = new[] { typeof(int), typeof(long), typeof(double), typeof(float), typeof(decimal) };

        var properties = typeof(InsightCohort)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            (numericTypes.Contains(prop.PropertyType) || prop.PropertyType == typeof(bool))
                .Should().BeTrue(
                    $"InsightCohort property '{prop.Name}' should be numeric or bool, not {prop.PropertyType.Name}");
        }
    }
}
