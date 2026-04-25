using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class EvidenceLinkTests
{
    private readonly Guid _intentCandidateId = Guid.NewGuid();
    private readonly Guid _sourceSpanId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateLink_WithValidData()
    {
        var link = new EvidenceLink(_intentCandidateId, _sourceSpanId, 0.8, "Contains deadline mention");

        link.Id.Should().NotBe(Guid.Empty);
        link.IntentCandidateId.Should().Be(_intentCandidateId);
        link.SourceSpanId.Should().Be(_sourceSpanId);
        link.Relevance.Should().Be(0.8);
        link.Rationale.Should().Be("Contains deadline mention");
    }

    [Fact]
    public void Constructor_ShouldDefaultRelevanceTo1()
    {
        var link = new EvidenceLink(_intentCandidateId, _sourceSpanId);

        link.Relevance.Should().Be(1.0);
    }

    [Fact]
    public void Constructor_ShouldAcceptNullRationale()
    {
        var link = new EvidenceLink(_intentCandidateId, _sourceSpanId, 0.5);

        link.Rationale.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyIntentCandidateId()
    {
        var act = () => new EvidenceLink(Guid.Empty, _sourceSpanId);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptySourceSpanId()
    {
        var act = () => new EvidenceLink(_intentCandidateId, Guid.Empty);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    public void Constructor_ShouldRejectRelevanceOutOfRange(double relevance)
    {
        var act = () => new EvidenceLink(_intentCandidateId, _sourceSpanId, relevance);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Constructor_ShouldAcceptRelevanceBoundaryValues(double relevance)
    {
        var link = new EvidenceLink(_intentCandidateId, _sourceSpanId, relevance);

        link.Relevance.Should().Be(relevance);
    }

    [Fact]
    public void Constructor_ShouldRejectRationaleExceeding500Characters()
    {
        var longRationale = new string('x', 501);
        var act = () => new EvidenceLink(_intentCandidateId, _sourceSpanId, 0.5, longRationale);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldTrimRationale()
    {
        var link = new EvidenceLink(_intentCandidateId, _sourceSpanId, 0.5, "  some rationale  ");

        link.Rationale.Should().Be("some rationale");
    }

    [Fact]
    public void Constructor_ShouldSetNullRationale_WhenWhitespace()
    {
        var link = new EvidenceLink(_intentCandidateId, _sourceSpanId, 0.5, "   ");

        link.Rationale.Should().BeNull();
    }

    [Fact]
    public void UpdateRelevance_ShouldUpdateValue()
    {
        var link = new EvidenceLink(_intentCandidateId, _sourceSpanId, 0.5);

        link.UpdateRelevance(0.9);

        link.Relevance.Should().Be(0.9);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void UpdateRelevance_ShouldRejectOutOfRange(double newRelevance)
    {
        var link = new EvidenceLink(_intentCandidateId, _sourceSpanId, 0.5);

        var act = () => link.UpdateRelevance(newRelevance);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }
}
