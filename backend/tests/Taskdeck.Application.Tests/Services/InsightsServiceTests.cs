using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class InsightsServiceTests
{
    private readonly Mock<IProposalOutcomeRepository> _repository;
    private readonly InsightsService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public InsightsServiceTests()
    {
        _repository = new Mock<IProposalOutcomeRepository>();
        _sut = new InsightsService(_repository.Object);
    }

    [Fact]
    public async Task GetProposalCohortAsync_EmptyOutcomes_ReturnsZeroCohort()
    {
        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProposalOutcome>());

        var cohort = await _sut.GetProposalCohortAsync(_userId, 30);

        cohort.AcceptedCount.Should().Be(0);
        cohort.EditedCount.Should().Be(0);
        cohort.RejectedCount.Should().Be(0);
        cohort.TotalCount.Should().Be(0);
        cohort.AcceptanceRate.Should().Be(0.0);
    }

    [Fact]
    public async Task GetProposalCohortAsync_MixedOutcomes_CountsCorrectly()
    {
        var outcomes = new List<ProposalOutcome>
        {
            CreateOutcome(OutcomeType.Approved, daysAgo: 2),
            CreateOutcome(OutcomeType.Approved, daysAgo: 5),
            CreateOutcome(OutcomeType.EditedThenApproved, daysAgo: 3),
            CreateOutcome(OutcomeType.Rejected, daysAgo: 1),
            CreateOutcome(OutcomeType.Rejected, daysAgo: 40),
        };

        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

        var cohort = await _sut.GetProposalCohortAsync(_userId, 30);

        cohort.AcceptedCount.Should().Be(2);
        cohort.EditedCount.Should().Be(1);
        cohort.RejectedCount.Should().Be(1);
        cohort.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task GetProposalCohortAsync_FiltersOutOldOutcomes()
    {
        var outcomes = new List<ProposalOutcome>
        {
            CreateOutcome(OutcomeType.Approved, daysAgo: 5),
            CreateOutcome(OutcomeType.Rejected, daysAgo: 35),
        };

        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

        var cohort = await _sut.GetProposalCohortAsync(_userId, 30);

        cohort.AcceptedCount.Should().Be(1);
        cohort.RejectedCount.Should().Be(0);
        cohort.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetProposalCohortAsync_NegativePeriodDefaultsTo30()
    {
        var outcomes = new List<ProposalOutcome>
        {
            CreateOutcome(OutcomeType.Approved, daysAgo: 25),
            CreateOutcome(OutcomeType.Approved, daysAgo: 35),
        };

        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

        var cohort = await _sut.GetProposalCohortAsync(_userId, -1);

        cohort.AcceptedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetProposalCohortAsync_LargePeriodClampedTo365()
    {
        var outcomes = new List<ProposalOutcome>
        {
            CreateOutcome(OutcomeType.Approved, daysAgo: 360),
            CreateOutcome(OutcomeType.Approved, daysAgo: 370),
        };

        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

        var cohort = await _sut.GetProposalCohortAsync(_userId, 9999);

        cohort.AcceptedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetProposalCohortAsync_IgnoredOutcomesExcludedFromCounts()
    {
        var outcomes = new List<ProposalOutcome>
        {
            CreateOutcome(OutcomeType.Approved, daysAgo: 1),
            CreateOutcome(OutcomeType.Ignored, daysAgo: 2),
            CreateOutcome(OutcomeType.Ignored, daysAgo: 3),
            CreateOutcome(OutcomeType.Rejected, daysAgo: 4),
        };

        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

        var cohort = await _sut.GetProposalCohortAsync(_userId, 30);

        cohort.AcceptedCount.Should().Be(1);
        cohort.RejectedCount.Should().Be(1);
        cohort.EditedCount.Should().Be(0);
        cohort.TotalCount.Should().Be(2, "Ignored outcomes should not contribute to TotalCount");
    }

    [Fact]
    public async Task GetMetricsAsync_EmptyOutcomes_ReturnsEmptyList()
    {
        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProposalOutcome>());

        var metrics = await _sut.GetMetricsAsync(_userId, 30);

        metrics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMetricsAsync_WithOutcomes_ReturnsBucketedMetrics()
    {
        var outcomes = new List<ProposalOutcome>
        {
            CreateOutcome(OutcomeType.Approved, daysAgo: 1),
            CreateOutcome(OutcomeType.Approved, daysAgo: 2),
            CreateOutcome(OutcomeType.Approved, daysAgo: 3),
            CreateOutcome(OutcomeType.EditedThenApproved, daysAgo: 1),
            CreateOutcome(OutcomeType.Rejected, daysAgo: 2),
        };

        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

        var metrics = await _sut.GetMetricsAsync(_userId, 30);

        metrics.Should().HaveCount(4);
        metrics.Should().Contain(m => m.MetricName == "proposal.acceptance_rate");
        metrics.Should().Contain(m => m.MetricName == "proposal.edit_rate");
        metrics.Should().Contain(m => m.MetricName == "proposal.rejection_rate");
        metrics.Should().Contain(m => m.MetricName == "proposal.generated_count");

        var totalMetric = metrics.First(m => m.MetricName == "proposal.generated_count");
        totalMetric.BucketedCount.Should().Be(5);
        totalMetric.TimePeriodDays.Should().Be(30);
        totalMetric.PromptVersion.Should().Be("v1.0");
    }

    [Fact]
    public async Task GetMetricsAsync_BucketingQuantizesSmallCounts()
    {
        var outcomes = new List<ProposalOutcome>
        {
            CreateOutcome(OutcomeType.Approved, daysAgo: 1),
            CreateOutcome(OutcomeType.Approved, daysAgo: 2),
        };

        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

        var metrics = await _sut.GetMetricsAsync(_userId, 30);

        var acceptanceMetric = metrics.First(m => m.MetricName == "proposal.acceptance_rate");
        acceptanceMetric.BucketedCount.Should().Be(5);
    }

    [Fact]
    public async Task GetMetricsAsync_AllMetricsAreValid()
    {
        var outcomes = new List<ProposalOutcome>
        {
            CreateOutcome(OutcomeType.Approved, daysAgo: 1),
        };

        _repository.Setup(r => r.GetAllByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcomes);

        var metrics = await _sut.GetMetricsAsync(_userId, 30);

        foreach (var metric in metrics)
        {
            metric.IsValid().Should().BeTrue($"metric {metric.MetricName} should pass validation");
        }
    }

    private ProposalOutcome CreateOutcome(OutcomeType outcomeType, int daysAgo)
    {
        var proposalId = Guid.NewGuid();
        var outcome = new ProposalOutcome(proposalId, outcomeType, _userId);
        SetDecidedAt(outcome, DateTimeOffset.UtcNow.AddDays(-daysAgo));
        return outcome;
    }

    private static void SetDecidedAt(ProposalOutcome outcome, DateTimeOffset value)
    {
        var prop = typeof(ProposalOutcome).GetProperty(nameof(ProposalOutcome.DecidedAt))
            ?? throw new InvalidOperationException($"Property {nameof(ProposalOutcome.DecidedAt)} not found on {nameof(ProposalOutcome)}");
        prop.SetValue(outcome, value);
    }
}
