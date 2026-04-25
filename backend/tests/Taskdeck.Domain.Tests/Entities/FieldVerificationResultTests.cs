using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class FieldVerificationResultTests
{
    [Fact]
    public void Constructor_ShouldCreateResult_WithRequiredFields()
    {
        var result = new FieldVerificationResult(
            "Title",
            VerificationStatus.Verified,
            originalConfidence: 0.9,
            adjustedConfidence: 0.9);

        result.FieldName.Should().Be("Title");
        result.Status.Should().Be(VerificationStatus.Verified);
        result.OriginalConfidence.Should().Be(0.9);
        result.AdjustedConfidence.Should().Be(0.9);
        result.SimilarityScore.Should().BeNull();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldCreateResult_WithAllFields()
    {
        var result = new FieldVerificationResult(
            "DueDate",
            VerificationStatus.Downgraded,
            originalConfidence: 0.95,
            adjustedConfidence: 0.7,
            similarityScore: 0.75,
            reason: "Fuzzy match below verification threshold");

        result.SimilarityScore.Should().Be(0.75);
        result.Reason.Should().Be("Fuzzy match below verification threshold");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFieldNameIsEmpty()
    {
        var act = () => new FieldVerificationResult("", VerificationStatus.Verified, 0.9, 0.9);

        act.Should().Throw<DomainException>()
            .WithMessage("FieldName cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenStatusIsInvalid()
    {
        var act = () => new FieldVerificationResult("Title", (VerificationStatus)99, 0.9, 0.9);

        act.Should().Throw<DomainException>()
            .WithMessage("VerificationStatus value is invalid");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ShouldThrow_WhenOriginalConfidenceOutOfRange(double confidence)
    {
        var act = () => new FieldVerificationResult("Title", VerificationStatus.Verified, confidence, 0.9);

        act.Should().Throw<DomainException>()
            .WithMessage("OriginalConfidence must be between 0.0 and 1.0");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ShouldThrow_WhenAdjustedConfidenceOutOfRange(double confidence)
    {
        var act = () => new FieldVerificationResult("Title", VerificationStatus.Verified, 0.9, confidence);

        act.Should().Throw<DomainException>()
            .WithMessage("AdjustedConfidence must be between 0.0 and 1.0");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ShouldThrow_WhenSimilarityScoreOutOfRange(double score)
    {
        var act = () => new FieldVerificationResult("Title", VerificationStatus.Verified, 0.9, 0.9, similarityScore: score);

        act.Should().Throw<DomainException>()
            .WithMessage("SimilarityScore must be between 0.0 and 1.0");
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForEqualResults()
    {
        var a = new FieldVerificationResult("Title", VerificationStatus.Verified, 0.9, 0.9, 0.95, "ok");
        var b = new FieldVerificationResult("Title", VerificationStatus.Verified, 0.9, 0.9, 0.95, "ok");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentResults()
    {
        var a = new FieldVerificationResult("Title", VerificationStatus.Verified, 0.9, 0.9);
        var b = new FieldVerificationResult("Title", VerificationStatus.Failed, 0.9, 0.0);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenComparedToNonResult()
    {
        var result = new FieldVerificationResult("Title", VerificationStatus.Verified, 0.9, 0.9);

        result.Equals("not a result").Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldAcceptFailedStatusWithZeroAdjusted()
    {
        var result = new FieldVerificationResult(
            "Description",
            VerificationStatus.Failed,
            originalConfidence: 0.8,
            adjustedConfidence: 0.0,
            reason: "Source text not found");

        result.Status.Should().Be(VerificationStatus.Failed);
        result.AdjustedConfidence.Should().Be(0.0);
    }
}
