using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Confidence;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Confidence;

public class ConfidenceBreakdownServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly ConfidenceBreakdownService _sut;

    public ConfidenceBreakdownServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _sut = new ConfidenceBreakdownService(_unitOfWorkMock.Object);
    }

    private static AutomationProposal CreateProposal(
        RiskLevel riskLevel = RiskLevel.Low,
        int expiryMinutes = 1440)
    {
        return new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            riskLevel,
            Guid.NewGuid().ToString(),
            Guid.NewGuid(),
            null,
            expiryMinutes);
    }

    private static AutomationProposalOperation CreateOperation(
        Guid proposalId,
        string actionType = "create",
        string targetType = "card",
        string? targetId = null)
    {
        return new AutomationProposalOperation(
            proposalId,
            0,
            actionType,
            targetType,
            "{}",
            Guid.NewGuid().ToString(),
            targetId);
    }

    #region GetBreakdownAsync

    [Fact]
    public async Task GetBreakdownAsync_ShouldReturnFailure_WhenProposalNotFound()
    {
        var proposalId = Guid.NewGuid();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _sut.GetBreakdownAsync(proposalId, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetBreakdownAsync_ShouldReturnBreakdown_WithFourComponents()
    {
        var proposal = CreateProposal();
        proposal.AddOperation(CreateOperation(proposal.Id, "create", "card"));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _sut.GetBreakdownAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value.Components.Should().HaveCount(4);
        result.Value.Components.Select(c => c.Key).Should().BeEquivalentTo(
            new[] { "Pattern match", "Reach", "Reversibility", "Recency" });
    }

    [Fact]
    public async Task GetBreakdownAsync_ShouldReturnDefaultThreshold()
    {
        var proposal = CreateProposal();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _sut.GetBreakdownAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value.Threshold.Should().Be(0.7);
    }

    [Fact]
    public async Task GetBreakdownAsync_ShouldReturnOverallBetweenZeroAndOne()
    {
        var proposal = CreateProposal();
        proposal.AddOperation(CreateOperation(proposal.Id));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _sut.GetBreakdownAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value.Overall.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public async Task GetBreakdownAsync_AllComponentValues_ShouldBeBetweenZeroAndOne()
    {
        var proposal = CreateProposal();
        proposal.AddOperation(CreateOperation(proposal.Id, "move", "card"));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _sut.GetBreakdownAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        foreach (var component in result.Value.Components)
        {
            component.Value.Should().BeInRange(0.0, 1.0,
                $"Component '{component.Key}' should be in [0, 1]");
        }
    }

    [Fact]
    public async Task GetBreakdownAsync_ShouldIncludeMeetsThreshold()
    {
        var proposal = CreateProposal(RiskLevel.Low);
        proposal.AddOperation(CreateOperation(proposal.Id, "create", "card"));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _sut.GetBreakdownAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value.MeetsThreshold.Should().Be(result.Value.Overall >= result.Value.Threshold);
    }

    #endregion

    #region ComputePatternMatch

    [Fact]
    public void ComputePatternMatch_ShouldReturn1_WhenAllActionsAreKnown()
    {
        var proposal = CreateProposal();
        proposal.AddOperation(CreateOperation(proposal.Id, "create"));
        proposal.AddOperation(CreateOperation(proposal.Id, "move"));

        var score = ConfidenceBreakdownService.ComputePatternMatch(proposal);

        score.Should().Be(1.0);
    }

    [Fact]
    public void ComputePatternMatch_ShouldReturn0_WhenNoActionsAreKnown()
    {
        var proposal = CreateProposal();
        proposal.AddOperation(CreateOperation(proposal.Id, "frobnicate"));
        proposal.AddOperation(CreateOperation(proposal.Id, "glurpify"));

        var score = ConfidenceBreakdownService.ComputePatternMatch(proposal);

        score.Should().Be(0.0);
    }

    [Fact]
    public void ComputePatternMatch_ShouldReturn0_5_WhenHalfActionsKnown()
    {
        var proposal = CreateProposal();
        proposal.AddOperation(CreateOperation(proposal.Id, "create"));
        proposal.AddOperation(CreateOperation(proposal.Id, "unknown_action"));

        var score = ConfidenceBreakdownService.ComputePatternMatch(proposal);

        score.Should().Be(0.5);
    }

    [Fact]
    public void ComputePatternMatch_ShouldReturn0_5_WhenNoOperations()
    {
        var proposal = CreateProposal();

        var score = ConfidenceBreakdownService.ComputePatternMatch(proposal);

        score.Should().Be(0.5);
    }

    #endregion

    #region ComputeReach

    [Fact]
    public void ComputeReach_ShouldReturn1_WhenNoOperations()
    {
        var proposal = CreateProposal();

        var score = ConfidenceBreakdownService.ComputeReach(proposal);

        score.Should().Be(1.0);
    }

    [Fact]
    public void ComputeReach_SingleTarget_ShouldScoreHigherThanMultipleTargets()
    {
        var proposal1 = CreateProposal();
        proposal1.AddOperation(CreateOperation(proposal1.Id, "move", "card", Guid.NewGuid().ToString()));

        var proposal2 = CreateProposal();
        var t1 = Guid.NewGuid().ToString();
        var t2 = Guid.NewGuid().ToString();
        var t3 = Guid.NewGuid().ToString();
        proposal2.AddOperation(CreateOperation(proposal2.Id, "move", "card", t1));
        proposal2.AddOperation(CreateOperation(proposal2.Id, "move", "card", t2));
        proposal2.AddOperation(CreateOperation(proposal2.Id, "move", "card", t3));

        var scoreSingle = ConfidenceBreakdownService.ComputeReach(proposal1);
        var scoreMulti = ConfidenceBreakdownService.ComputeReach(proposal2);

        scoreSingle.Should().BeGreaterThan(scoreMulti);
    }

    [Fact]
    public void ComputeReach_ShouldBeBetweenZeroAndOne()
    {
        var proposal = CreateProposal();
        for (int i = 0; i < 100; i++)
        {
            proposal.AddOperation(CreateOperation(proposal.Id, "move", "card", Guid.NewGuid().ToString()));
        }

        var score = ConfidenceBreakdownService.ComputeReach(proposal);

        score.Should().BeInRange(0.0, 1.0);
    }

    #endregion

    #region ComputeReversibility

    [Fact]
    public void ComputeReversibility_LowRisk_CreateAction_ShouldScoreHigh()
    {
        var proposal = CreateProposal(RiskLevel.Low);
        proposal.AddOperation(CreateOperation(proposal.Id, "create"));

        var score = ConfidenceBreakdownService.ComputeReversibility(proposal);

        score.Should().BeGreaterThanOrEqualTo(0.8);
    }

    [Fact]
    public void ComputeReversibility_CriticalRisk_DeleteAction_ShouldScoreLow()
    {
        var proposal = CreateProposal(RiskLevel.Critical);
        proposal.AddOperation(CreateOperation(proposal.Id, "delete"));

        var score = ConfidenceBreakdownService.ComputeReversibility(proposal);

        score.Should().BeLessThanOrEqualTo(0.3);
    }

    [Fact]
    public void ComputeReversibility_HighRisk_ShouldScoreLowerThanLowRisk()
    {
        var lowRisk = CreateProposal(RiskLevel.Low);
        lowRisk.AddOperation(CreateOperation(lowRisk.Id, "create"));

        var highRisk = CreateProposal(RiskLevel.High);
        highRisk.AddOperation(CreateOperation(highRisk.Id, "create"));

        var scoreLow = ConfidenceBreakdownService.ComputeReversibility(lowRisk);
        var scoreHigh = ConfidenceBreakdownService.ComputeReversibility(highRisk);

        scoreLow.Should().BeGreaterThan(scoreHigh);
    }

    [Fact]
    public void ComputeReversibility_ShouldBeBetweenZeroAndOne()
    {
        foreach (var risk in Enum.GetValues<RiskLevel>())
        {
            var proposal = CreateProposal(risk);
            proposal.AddOperation(CreateOperation(proposal.Id, "archive"));

            var score = ConfidenceBreakdownService.ComputeReversibility(proposal);
            score.Should().BeInRange(0.0, 1.0, $"Risk level {risk} should produce score in [0,1]");
        }
    }

    #endregion

    #region ComputeRecency

    [Fact]
    public void ComputeRecency_FreshProposal_ShouldBeCloseToOne()
    {
        // A freshly created proposal with long expiry should be near 1.0
        var proposal = CreateProposal(expiryMinutes: 1440);

        var score = ConfidenceBreakdownService.ComputeRecency(proposal);

        score.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void ComputeRecency_ShouldBeBetweenZeroAndOne()
    {
        var proposal = CreateProposal(expiryMinutes: 60);

        var score = ConfidenceBreakdownService.ComputeRecency(proposal);

        score.Should().BeInRange(0.0, 1.0);
    }

    #endregion

    #region ComputeOverall

    [Fact]
    public void ComputeOverall_ShouldReturnWeightedAverage()
    {
        var components = new ConfidenceComponent[]
        {
            new("Pattern match", 1.0),
            new("Reach", 1.0),
            new("Reversibility", 1.0),
            new("Recency", 1.0)
        };

        var overall = ConfidenceBreakdownService.ComputeOverall(components);

        overall.Should().Be(1.0);
    }

    [Fact]
    public void ComputeOverall_AllZeros_ShouldReturnZero()
    {
        var components = new ConfidenceComponent[]
        {
            new("Pattern match", 0.0),
            new("Reach", 0.0),
            new("Reversibility", 0.0),
            new("Recency", 0.0)
        };

        var overall = ConfidenceBreakdownService.ComputeOverall(components);

        overall.Should().Be(0.0);
    }

    [Fact]
    public void ComputeOverall_ShouldBeBetweenZeroAndOne()
    {
        var components = new ConfidenceComponent[]
        {
            new("Pattern match", 0.3),
            new("Reach", 0.5),
            new("Reversibility", 0.8),
            new("Recency", 0.1)
        };

        var overall = ConfidenceBreakdownService.ComputeOverall(components);

        overall.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void ComputeOverall_EmptyComponents_ShouldReturnZero()
    {
        var overall = ConfidenceBreakdownService.ComputeOverall(Array.Empty<ConfidenceComponent>());

        overall.Should().Be(0.0);
    }

    [Fact]
    public void ComputeOverall_UnknownComponents_ShouldReturnZero()
    {
        var components = new ConfidenceComponent[]
        {
            new("Unknown component A", 0.9),
            new("Unknown component B", 0.8)
        };

        var overall = ConfidenceBreakdownService.ComputeOverall(components);

        overall.Should().Be(0.0);
    }

    [Fact]
    public void ComputeOverall_ReversibilityWeightedHighest()
    {
        // Reversibility has weight 0.35 (highest), Pattern match 0.30
        // If Reversibility is high and all others are zero:
        var highReversibility = new ConfidenceComponent[]
        {
            new("Pattern match", 0.0),
            new("Reach", 0.0),
            new("Reversibility", 1.0),
            new("Recency", 0.0)
        };

        // If Pattern match is high and all others are zero:
        var highPattern = new ConfidenceComponent[]
        {
            new("Pattern match", 1.0),
            new("Reach", 0.0),
            new("Reversibility", 0.0),
            new("Recency", 0.0)
        };

        var overallReversibility = ConfidenceBreakdownService.ComputeOverall(highReversibility);
        var overallPattern = ConfidenceBreakdownService.ComputeOverall(highPattern);

        overallReversibility.Should().BeGreaterThan(overallPattern,
            "Reversibility has higher weight than Pattern match");
    }

    #endregion

    #region GenerateNote

    [Fact]
    public void GenerateNote_ClearlyAboveThreshold_ShouldReturnNull()
    {
        var note = ConfidenceBreakdownService.GenerateNote(0.9, 0.7);

        note.Should().BeNull();
    }

    [Fact]
    public void GenerateNote_JustAboveThreshold_ShouldReturnNote()
    {
        var note = ConfidenceBreakdownService.GenerateNote(0.75, 0.7);

        note.Should().NotBeNull();
        note.Should().Contain("just above");
    }

    [Fact]
    public void GenerateNote_JustBelowThreshold_ShouldReturnNote()
    {
        var note = ConfidenceBreakdownService.GenerateNote(0.65, 0.7);

        note.Should().NotBeNull();
        note.Should().Contain("just below");
    }

    [Fact]
    public void GenerateNote_ClearlyBelowThreshold_ShouldReturnNote()
    {
        var note = ConfidenceBreakdownService.GenerateNote(0.3, 0.7);

        note.Should().NotBeNull();
        note.Should().Contain("below the threshold");
    }

    [Fact]
    public void GenerateNote_ExactlyAtThreshold_ShouldReturnJustAbove()
    {
        var note = ConfidenceBreakdownService.GenerateNote(0.7, 0.7);

        // overall >= threshold, and overall - threshold (0.0) < nearThresholdBand (0.1)
        note.Should().NotBeNull();
        note.Should().Contain("just above");
    }

    #endregion

    #region ProposalType integration

    [Fact]
    public async Task GetBreakdownAsync_ForHighRiskDestructiveProposal_ShouldHaveLowReversibility()
    {
        var proposal = CreateProposal(RiskLevel.High);
        proposal.AddOperation(CreateOperation(proposal.Id, "delete", "card"));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _sut.GetBreakdownAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        var reversibility = result.Value.Components.First(c => c.Key == "Reversibility");
        reversibility.Value.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task GetBreakdownAsync_ForLowRiskCreateProposal_ShouldHaveHighReversibility()
    {
        var proposal = CreateProposal(RiskLevel.Low);
        proposal.AddOperation(CreateOperation(proposal.Id, "create", "card"));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _sut.GetBreakdownAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        var reversibility = result.Value.Components.First(c => c.Key == "Reversibility");
        reversibility.Value.Should().BeGreaterThanOrEqualTo(0.8);
    }

    [Fact]
    public async Task GetBreakdownAsync_ForMultiTargetProposal_ShouldHaveLowerReach()
    {
        var proposal = CreateProposal(RiskLevel.Low);
        for (int i = 0; i < 5; i++)
        {
            proposal.AddOperation(CreateOperation(proposal.Id, "move", "card", Guid.NewGuid().ToString()));
        }

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _sut.GetBreakdownAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        var reach = result.Value.Components.First(c => c.Key == "Reach");
        reach.Value.Should().BeLessThan(0.8);
    }

    #endregion
}
