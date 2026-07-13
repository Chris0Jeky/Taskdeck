using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class ArtefactExtractionTests
{
    [Fact]
    public void Constructor_ShouldCreateImmutableBoundedRecord()
    {
        var artefactId = Guid.NewGuid();

        var extraction = new ArtefactExtraction(
            artefactId,
            "PdfPig",
            "0.1.15",
            ["page-limit", "character-limit"],
            "first\nsecond");

        extraction.SourceArtefactId.Should().Be(artefactId);
        extraction.ExtractorName.Should().Be("PdfPig");
        extraction.ExtractorVersion.Should().Be("0.1.15");
        extraction.Warnings.Should().Equal("page-limit", "character-limit");
        extraction.ExtractedText.Should().Be("first\nsecond");
        extraction.TextLength.Should().Be(12);
    }

    [Fact]
    public void Constructor_ShouldRejectCarriageReturnsBeforePersistence()
    {
        var act = () => new ArtefactExtraction(
            Guid.NewGuid(),
            "PlainText",
            "1.0",
            [],
            "first\r\nsecond");

        act.Should().Throw<DomainException>()
            .WithMessage("*LF line endings*");
    }

    [Fact]
    public void Constructor_ShouldRejectTextAbovePersistenceCap()
    {
        var act = () => new ArtefactExtraction(
            Guid.NewGuid(),
            "PlainText",
            "1.0",
            [],
            new string('x', ArtefactExtraction.MaxExtractedTextLength + 1));

        act.Should().Throw<DomainException>()
            .WithMessage($"*{ArtefactExtraction.MaxExtractedTextLength}*");
    }

    [Fact]
    public void Constructor_ShouldRejectUnpairedSurrogate()
    {
        var act = () => new ArtefactExtraction(
            Guid.NewGuid(),
            "PlainText",
            "1.0",
            [],
            "invalid\uD83D");

        act.Should().Throw<DomainException>()
            .WithMessage("*valid UTF-16*");
    }

    [Fact]
    public void Constructor_ShouldRejectUnboundedWarnings()
    {
        var warnings = Enumerable.Range(0, ArtefactExtraction.MaxWarningCount + 1)
            .Select(index => $"warning-{index}");

        var act = () => new ArtefactExtraction(
            Guid.NewGuid(),
            "PlainText",
            "1.0",
            warnings,
            string.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage($"*{ArtefactExtraction.MaxWarningCount}*");
    }
}
