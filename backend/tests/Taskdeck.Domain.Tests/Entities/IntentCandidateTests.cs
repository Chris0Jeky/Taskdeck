using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class IntentCandidateTests
{
    private readonly Guid _envelopeId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateCandidate_WithValidData()
    {
        var candidate = new IntentCandidate(_envelopeId, "Create API review card", 0.85, 0, "create-card");

        candidate.Id.Should().NotBe(Guid.Empty);
        candidate.EnvelopeId.Should().Be(_envelopeId);
        candidate.Label.Should().Be("Create API review card");
        candidate.Confidence.Should().Be(0.85);
        candidate.Rank.Should().Be(0);
        candidate.ActionType.Should().Be("create-card");
        candidate.EvidenceLinks.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldAcceptNullActionType()
    {
        var candidate = new IntentCandidate(_envelopeId, "Informational note", 0.5, 1);

        candidate.ActionType.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyEnvelopeId()
    {
        var act = () => new IntentCandidate(Guid.Empty, "Label", 0.5, 0);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyLabel()
    {
        var act = () => new IntentCandidate(_envelopeId, "", 0.5, 0);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectLabelExceeding500Characters()
    {
        var longLabel = new string('x', 501);
        var act = () => new IntentCandidate(_envelopeId, longLabel, 0.5, 0);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    public void Constructor_ShouldRejectConfidenceOutOfRange(double confidence)
    {
        var act = () => new IntentCandidate(_envelopeId, "Label", confidence, 0);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Constructor_ShouldAcceptConfidenceBoundaryValues(double confidence)
    {
        var candidate = new IntentCandidate(_envelopeId, "Label", confidence, 0);

        candidate.Confidence.Should().Be(confidence);
    }

    [Fact]
    public void Constructor_ShouldRejectNegativeRank()
    {
        var act = () => new IntentCandidate(_envelopeId, "Label", 0.5, -1);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectActionTypeExceeding100Characters()
    {
        var longAction = new string('x', 101);
        var act = () => new IntentCandidate(_envelopeId, "Label", 0.5, 0, longAction);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldTrimActionType()
    {
        var candidate = new IntentCandidate(_envelopeId, "Label", 0.5, 0, "  create-card  ");

        candidate.ActionType.Should().Be("create-card");
    }

    [Fact]
    public void Constructor_ShouldSetNullActionType_WhenWhitespace()
    {
        var candidate = new IntentCandidate(_envelopeId, "Label", 0.5, 0, "   ");

        candidate.ActionType.Should().BeNull();
    }

    [Fact]
    public void UpdateConfidence_ShouldUpdateValue()
    {
        var candidate = new IntentCandidate(_envelopeId, "Label", 0.5, 0);

        candidate.UpdateConfidence(0.9);

        candidate.Confidence.Should().Be(0.9);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void UpdateConfidence_ShouldRejectOutOfRange(double newConfidence)
    {
        var candidate = new IntentCandidate(_envelopeId, "Label", 0.5, 0);

        var act = () => candidate.UpdateConfidence(newConfidence);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void AddEvidenceLink_ShouldAddLink()
    {
        var candidate = new IntentCandidate(_envelopeId, "Label", 0.5, 0);
        var spanId = Guid.NewGuid();
        var link = new EvidenceLink(candidate.Id, spanId, 0.8, "Key evidence");

        candidate.AddEvidenceLink(link);

        candidate.EvidenceLinks.Should().HaveCount(1);
        candidate.EvidenceLinks[0].Should().Be(link);
    }

    [Fact]
    public void AddEvidenceLink_ShouldRejectLinkBelongingToDifferentCandidate()
    {
        var candidate = new IntentCandidate(_envelopeId, "Label", 0.5, 0);
        var otherCandidateId = Guid.NewGuid();
        var link = new EvidenceLink(otherCandidateId, Guid.NewGuid(), 0.8);

        var act = () => candidate.AddEvidenceLink(link);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }
}
