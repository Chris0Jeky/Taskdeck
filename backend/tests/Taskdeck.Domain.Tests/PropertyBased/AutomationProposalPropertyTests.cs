using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for AutomationProposal entity state machine and invariants.
/// Replay: set Replay = "seed,size" on any [Property] to reproduce a failing case.
/// </summary>
public class AutomationProposalPropertyTests
{
    private const int MaxTests = 200;

    [Property(MaxTest = MaxTests)]
    public Property ValidConstruction_AlwaysCreatesPendingProposal()
    {
        return Prop.ForAll(
            ValidSummaryArb(),
            Arb.From(Gen.Elements(
                ProposalSourceType.Queue, ProposalSourceType.Chat, ProposalSourceType.Manual)),
            Arb.From(Gen.Elements(
                RiskLevel.Low, RiskLevel.Medium, RiskLevel.High, RiskLevel.Critical)),
            (summary, sourceType, riskLevel) =>
            {
                var userId = Guid.NewGuid();
                var correlationId = Guid.NewGuid().ToString();
                var proposal = new AutomationProposal(sourceType, userId, summary, riskLevel, correlationId);
                proposal.Status.Should().Be(ProposalStatus.PendingReview);
                proposal.Summary.Should().Be(summary);
                proposal.SourceType.Should().Be(sourceType);
                proposal.RiskLevel.Should().Be(riskLevel);
                proposal.RequestedByUserId.Should().Be(userId);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyUserId_AlwaysThrows()
    {
        var act = () => new AutomationProposal(
            ProposalSourceType.Queue, Guid.Empty, "Valid summary",
            RiskLevel.Low, Guid.NewGuid().ToString());
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        return true.ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyOrWhitespaceSummary_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n")),
            summary =>
            {
                var act = () => new AutomationProposal(
                    ProposalSourceType.Queue, Guid.NewGuid(), summary,
                    RiskLevel.Low, Guid.NewGuid().ToString());
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property SummaryExceeding500Chars_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(501, 1000).Select(len => new string('s', len))),
            longSummary =>
            {
                var act = () => new AutomationProposal(
                    ProposalSourceType.Queue, Guid.NewGuid(), longSummary,
                    RiskLevel.Low, Guid.NewGuid().ToString());
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ZeroOrNegativeExpiryMinutes_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(-100, 0)),
            expiryMinutes =>
            {
                var act = () => new AutomationProposal(
                    ProposalSourceType.Queue, Guid.NewGuid(), "Valid",
                    RiskLevel.Low, Guid.NewGuid().ToString(),
                    expiryMinutes: expiryMinutes);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ApproveFromNonPending_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("approved", "rejected", "expired")),
            priorState =>
            {
                var proposal = CreatePendingProposal();
                var deciderId = Guid.NewGuid();

                // Transition to non-pending state
                switch (priorState)
                {
                    case "approved":
                        proposal.Approve(deciderId);
                        break;
                    case "rejected":
                        proposal.Reject(deciderId);
                        break;
                    case "expired":
                        proposal.Expire();
                        break;
                }

                // Attempt to approve from non-pending state
                var act = () => proposal.Approve(Guid.NewGuid());
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property RejectFromNonPending_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("approved", "rejected", "expired")),
            priorState =>
            {
                var proposal = CreatePendingProposal();
                var deciderId = Guid.NewGuid();

                switch (priorState)
                {
                    case "approved":
                        proposal.Approve(deciderId);
                        break;
                    case "rejected":
                        proposal.Reject(deciderId);
                        break;
                    case "expired":
                        proposal.Expire();
                        break;
                }

                var act = () => proposal.Reject(Guid.NewGuid());
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property HighRiskReject_WithoutReason_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements(RiskLevel.High, RiskLevel.Critical)),
            riskLevel =>
            {
                var proposal = CreatePendingProposal(riskLevel: riskLevel);
                var act = () => proposal.Reject(Guid.NewGuid());
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property LowMediumRiskReject_WithoutReason_Succeeds()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements(RiskLevel.Low, RiskLevel.Medium)),
            riskLevel =>
            {
                var proposal = CreatePendingProposal(riskLevel: riskLevel);
                proposal.Reject(Guid.NewGuid());
                proposal.Status.Should().Be(ProposalStatus.Rejected);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property MarkAsApplied_OnlyFromApproved()
    {
        var proposal = CreatePendingProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();
        proposal.Status.Should().Be(ProposalStatus.Applied);
        proposal.AppliedAt.Should().NotBeNull();
        return true.ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property MarkAsFailed_OnlyFromApproved()
    {
        var proposal = CreatePendingProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("Something went wrong");
        proposal.Status.Should().Be(ProposalStatus.Failed);
        proposal.FailureReason.Should().Be("Something went wrong");
        return true.ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property MarkAsFailed_EmptyReason_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t")),
            reason =>
            {
                var proposal = CreatePendingProposal();
                proposal.Approve(Guid.NewGuid());
                var act = () => proposal.MarkAsFailed(reason);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property MarkAsApplied_FromPending_AlwaysThrows()
    {
        var proposal = CreatePendingProposal();
        var act = () => proposal.MarkAsApplied();
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
        return true.ToProperty();
    }

    private static AutomationProposal CreatePendingProposal(RiskLevel riskLevel = RiskLevel.Low)
    {
        return new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Test summary",
            riskLevel,
            Guid.NewGuid().ToString());
    }

    private static Arbitrary<string> ValidSummaryArb()
    {
        var gen = Gen.Choose(1, 500)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(
                    'a', 'b', 'c', 'A', 'B', 'C', '1', '2', '3', ' ', '-', '_'))
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return Arb.From(gen);
    }
}
