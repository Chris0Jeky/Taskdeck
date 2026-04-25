using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class SourceBlockTests
{
    private readonly Guid _envelopeId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateBlock_WithValidData()
    {
        var block = new SourceBlock(_envelopeId, 0, "Some text content", "capture", "ref-123");

        block.Id.Should().NotBe(Guid.Empty);
        block.EnvelopeId.Should().Be(_envelopeId);
        block.Position.Should().Be(0);
        block.Content.Should().Be("Some text content");
        block.SourceType.Should().Be("capture");
        block.SourceReferenceId.Should().Be("ref-123");
        block.Spans.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyEnvelopeId()
    {
        var act = () => new SourceBlock(Guid.Empty, 0, "content", "capture");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectNegativePosition()
    {
        var act = () => new SourceBlock(_envelopeId, -1, "content", "capture");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyContent()
    {
        var act = () => new SourceBlock(_envelopeId, 0, "", "capture");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectWhitespaceContent()
    {
        var act = () => new SourceBlock(_envelopeId, 0, "   ", "capture");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectContentExceeding50000Characters()
    {
        var longContent = new string('x', 50_001);
        var act = () => new SourceBlock(_envelopeId, 0, longContent, "capture");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptySourceType()
    {
        var act = () => new SourceBlock(_envelopeId, 0, "content", "");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectSourceTypeExceeding50Characters()
    {
        var longType = new string('x', 51);
        var act = () => new SourceBlock(_envelopeId, 0, "content", longType);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldTrimSourceReferenceId()
    {
        var block = new SourceBlock(_envelopeId, 0, "content", "capture", "  ref-456  ");

        block.SourceReferenceId.Should().Be("ref-456");
    }

    [Fact]
    public void Constructor_ShouldSetNullSourceReferenceId_WhenWhitespace()
    {
        var block = new SourceBlock(_envelopeId, 0, "content", "capture", "   ");

        block.SourceReferenceId.Should().BeNull();
    }

    [Fact]
    public void AddSpan_ShouldCreateAndReturnSpan()
    {
        var block = new SourceBlock(_envelopeId, 0, "Hello, world!", "capture");

        var span = block.AddSpan(0, 5, "Hello");

        span.Should().NotBeNull();
        span.SourceBlockId.Should().Be(block.Id);
        span.EnvelopeId.Should().Be(_envelopeId);
        span.StartOffset.Should().Be(0);
        span.EndOffset.Should().Be(5);
        span.SnippetText.Should().Be("Hello");
        block.Spans.Should().HaveCount(1);
    }

    [Fact]
    public void AddSpan_ShouldRejectEndOffsetBeyondContentLength()
    {
        var block = new SourceBlock(_envelopeId, 0, "Short", "capture");

        var act = () => block.AddSpan(0, 100, "Short");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void AddSpan_ShouldRejectStartOffsetBeyondContentLength()
    {
        var block = new SourceBlock(_envelopeId, 0, "Short", "capture");

        var act = () => block.AddSpan(100, 200, "x");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void AddSpan_ShouldAllowMultipleSpans()
    {
        var block = new SourceBlock(_envelopeId, 0, "Hello, world! Goodbye.", "capture");

        block.AddSpan(0, 5, "Hello");
        block.AddSpan(7, 12, "world");

        block.Spans.Should().HaveCount(2);
    }

    [Fact]
    public void AddSpan_ShouldRejectSnippetNotMatchingContent()
    {
        var block = new SourceBlock(_envelopeId, 0, "Hello, world!", "capture");

        var act = () => block.AddSpan(0, 5, "Wrong");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void AddSpan_ShouldRejectNegativeStartOffset()
    {
        var block = new SourceBlock(_envelopeId, 0, "Hello, world!", "capture");

        var act = () => block.AddSpan(-1, 5, "Hello");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void AddSpan_ShouldRejectEndOffsetNotGreaterThanStartOffset()
    {
        var block = new SourceBlock(_envelopeId, 0, "Hello, world!", "capture");

        var act = () => block.AddSpan(5, 5, "");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }
}
