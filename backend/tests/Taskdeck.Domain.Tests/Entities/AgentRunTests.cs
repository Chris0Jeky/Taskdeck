using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class AgentRunTests
{
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateRun_WithValidParameters()
    {
        var run = new AgentRun(_profileId, _userId, "Triage inbox items");

        run.AgentProfileId.Should().Be(_profileId);
        run.UserId.Should().Be(_userId);
        run.Objective.Should().Be("Triage inbox items");
        run.TriggerType.Should().Be("manual");
        run.Status.Should().Be(AgentRunStatus.Queued);
        run.BoardId.Should().BeNull();
        run.Summary.Should().BeNull();
        run.FailureReason.Should().BeNull();
        run.ProposalId.Should().BeNull();
        run.StepsExecuted.Should().Be(0);
        run.TokensUsed.Should().Be(0);
        run.ApproxCostUsd.Should().BeNull();
        run.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenProfileIdIsEmpty()
    {
        var act = () => new AgentRun(Guid.Empty, _userId, "Test");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*AgentProfileId*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdIsEmpty()
    {
        var act = () => new AgentRun(_profileId, Guid.Empty, "Test");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*UserId*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenObjectiveIsEmpty()
    {
        var act = () => new AgentRun(_profileId, _userId, "");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*Objective*");
    }

    [Fact]
    public void TransitionTo_ShouldUpdateStatus()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");

        run.TransitionTo(AgentRunStatus.GatheringContext);

        run.Status.Should().Be(AgentRunStatus.GatheringContext);
    }

    [Fact]
    public void TransitionTo_ShouldSetCompletedAt_WhenTerminal()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");

        run.TransitionTo(AgentRunStatus.Completed, "Done");

        run.Status.Should().Be(AgentRunStatus.Completed);
        run.Summary.Should().Be("Done");
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void TransitionTo_ShouldThrow_WhenAlreadyTerminal()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");
        run.TransitionTo(AgentRunStatus.Completed);

        var act = () => run.TransitionTo(AgentRunStatus.GatheringContext);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void AttachProposal_ShouldSetProposalId()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");
        var proposalId = Guid.NewGuid();

        run.AttachProposal(proposalId, "Proposal summary");

        run.ProposalId.Should().Be(proposalId);
        run.Summary.Should().Be("Proposal summary");
    }

    [Fact]
    public void AttachProposal_ShouldThrow_WhenProposalIdIsEmpty()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");

        var act = () => run.AttachProposal(Guid.Empty);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void MarkFailed_ShouldSetFailureReasonAndStatus()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");

        run.MarkFailed("Something went wrong");

        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureReason.Should().Be("Something went wrong");
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailed_ShouldThrow_WhenReasonIsEmpty()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");

        var act = () => run.MarkFailed("");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void IncrementSteps_ShouldIncreaseCount()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");

        run.IncrementSteps();
        run.IncrementSteps(3);

        run.StepsExecuted.Should().Be(4);
    }

    [Fact]
    public void AddTokenUsage_ShouldAccumulate()
    {
        var run = new AgentRun(_profileId, _userId, "Test objective");

        run.AddTokenUsage(100, 0.01m);
        run.AddTokenUsage(200, 0.02m);

        run.TokensUsed.Should().Be(300);
        run.ApproxCostUsd.Should().Be(0.03m);
    }
}
