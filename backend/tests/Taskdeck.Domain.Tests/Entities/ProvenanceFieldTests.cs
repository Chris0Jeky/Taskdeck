using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ProvenanceFieldTests
{
    private readonly Guid _provenanceId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateInferredField_WithValidData()
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.85, _provenanceId);

        field.FieldName.Should().Be("Title");
        field.Kind.Should().Be(ProvenanceKind.Inferred);
        field.Confidence.Should().Be(0.85);
        field.ProposalProvenanceId.Should().Be(_provenanceId);
        field.ExtractiveQuote.Should().BeNull();
        field.EvidenceLinks.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldCreateExtractiveField_WithQuote()
    {
        var field = new ProvenanceField(
            "DueDate",
            ProvenanceKind.Extractive,
            0.95,
            _provenanceId,
            "due by next Friday");

        field.Kind.Should().Be(ProvenanceKind.Extractive);
        field.ExtractiveQuote.Should().Be("due by next Friday");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFieldNameIsEmpty()
    {
        var act = () => new ProvenanceField("", ProvenanceKind.Inferred, 0.5, _provenanceId);

        act.Should().Throw<DomainException>()
            .WithMessage("FieldName cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFieldNameExceedsMaxLength()
    {
        var longName = new string('x', 101);

        var act = () => new ProvenanceField(longName, ProvenanceKind.Inferred, 0.5, _provenanceId);

        act.Should().Throw<DomainException>()
            .WithMessage("FieldName cannot exceed 100 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenKindIsInvalid()
    {
        var act = () => new ProvenanceField("Title", (ProvenanceKind)99, 0.5, _provenanceId);

        act.Should().Throw<DomainException>()
            .WithMessage("ProvenanceKind value is invalid");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    public void Constructor_ShouldThrow_WhenConfidenceOutOfRange(double confidence)
    {
        var act = () => new ProvenanceField("Title", ProvenanceKind.Inferred, confidence, _provenanceId);

        act.Should().Throw<DomainException>()
            .WithMessage("Confidence must be between 0.0 and 1.0");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Constructor_ShouldThrow_WhenConfidenceIsNonFinite(double confidence)
    {
        var act = () => new ProvenanceField("Title", ProvenanceKind.Inferred, confidence, _provenanceId);

        act.Should().Throw<DomainException>()
            .WithMessage("Confidence must be between 0.0 and 1.0");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Constructor_ShouldAccept_ValidConfidenceBoundaries(double confidence)
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, confidence, _provenanceId);

        field.Confidence.Should().Be(confidence);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenProvenanceIdIsEmpty()
    {
        var act = () => new ProvenanceField("Title", ProvenanceKind.Inferred, 0.5, Guid.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("ProposalProvenanceId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenExtractiveQuoteIsMissing_ForExtractiveKind()
    {
        var act = () => new ProvenanceField("Title", ProvenanceKind.Extractive, 0.9, _provenanceId);

        act.Should().Throw<DomainException>()
            .WithMessage("ExtractiveQuote is required for Extractive provenance kind");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenExtractiveQuoteProvided_ForInferredKind()
    {
        var act = () => new ProvenanceField(
            "Title",
            ProvenanceKind.Inferred,
            0.9,
            _provenanceId,
            "source quote");

        act.Should().Throw<DomainException>()
            .WithMessage("ExtractiveQuote is only valid for Extractive provenance kind");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenExtractiveQuoteExceedsMaxLength()
    {
        var longQuote = new string('q', 2001);

        var act = () => new ProvenanceField("Title", ProvenanceKind.Extractive, 0.9, _provenanceId, longQuote);

        act.Should().Throw<DomainException>()
            .WithMessage("ExtractiveQuote cannot exceed 2000 characters");
    }

    [Fact]
    public void AddEvidenceLink_ShouldAddLink()
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.9, _provenanceId);
        var link = new ProvenanceEvidenceLink("InboxCapture", "cap-123", field.Id);

        field.AddEvidenceLink(link);

        field.EvidenceLinks.Should().ContainSingle().Which.Should().Be(link);
    }

    [Fact]
    public void AddEvidenceLink_ShouldThrow_WhenLinkBelongsToDifferentField()
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.9, _provenanceId);
        var otherFieldId = Guid.NewGuid();
        var link = new ProvenanceEvidenceLink("InboxCapture", "cap-456", otherFieldId);

        var act = () => field.AddEvidenceLink(link);

        act.Should().Throw<DomainException>()
            .WithMessage("EvidenceLink does not belong to this ProvenanceField");
    }

    [Fact]
    public void AddEvidenceLink_ShouldThrow_WhenLinkIsNull()
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.9, _provenanceId);

        var act = () => field.AddEvidenceLink(null!);

        act.Should().Throw<DomainException>()
            .WithMessage("EvidenceLink cannot be null");
    }

    [Fact]
    public void DowngradeConfidence_ShouldReduceConfidence()
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.9, _provenanceId);

        field.DowngradeConfidence(0.5);

        field.Confidence.Should().Be(0.5);
    }

    [Fact]
    public void DowngradeConfidence_ShouldThrow_WhenNewConfidenceIsHigherOrEqual()
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.5, _provenanceId);

        var act = () => field.DowngradeConfidence(0.5);

        act.Should().Throw<DomainException>()
            .WithMessage("New confidence must be lower than current confidence for a downgrade")
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void DowngradeConfidence_ShouldThrow_WhenNewConfidenceOutOfRange()
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.9, _provenanceId);

        var act = () => field.DowngradeConfidence(-0.1);

        act.Should().Throw<DomainException>()
            .WithMessage("Confidence must be between 0.0 and 1.0");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DowngradeConfidence_ShouldThrow_WhenNewConfidenceIsNonFinite(double confidence)
    {
        var field = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.9, _provenanceId);

        var act = () => field.DowngradeConfidence(confidence);

        act.Should().Throw<DomainException>()
            .WithMessage("Confidence must be between 0.0 and 1.0");
    }
}
