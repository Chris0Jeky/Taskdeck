using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class SourceSpanTests
{
    private readonly Guid _sourceBlockId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateSpan_WithValidData()
    {
        var span = new SourceSpan(_sourceBlockId, 0, 10, "hello world".Substring(0, 10));

        span.Id.Should().NotBe(Guid.Empty);
        span.SourceBlockId.Should().Be(_sourceBlockId);
        span.StartOffset.Should().Be(0);
        span.EndOffset.Should().Be(10);
        span.SnippetText.Should().Be("hello worl");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptySourceBlockId()
    {
        var act = () => new SourceSpan(Guid.Empty, 0, 10, "snippet");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectNegativeStartOffset()
    {
        var act = () => new SourceSpan(_sourceBlockId, -1, 10, "snippet");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectNegativeEndOffset()
    {
        var act = () => new SourceSpan(_sourceBlockId, 0, -1, "snippet");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEndOffsetEqualToStartOffset()
    {
        var act = () => new SourceSpan(_sourceBlockId, 5, 5, "snippet");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEndOffsetLessThanStartOffset()
    {
        var act = () => new SourceSpan(_sourceBlockId, 10, 5, "snippet");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptySnippetText()
    {
        var act = () => new SourceSpan(_sourceBlockId, 0, 10, "");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectNullSnippetText()
    {
        var act = () => new SourceSpan(_sourceBlockId, 0, 10, null!);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectSnippetTextExceeding2000Characters()
    {
        var longSnippet = new string('x', 2001);
        var act = () => new SourceSpan(_sourceBlockId, 0, 10, longSnippet);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Length_ShouldReturnDifferenceBetweenEndAndStartOffset()
    {
        var span = new SourceSpan(_sourceBlockId, 5, 15, "ten chars!");

        span.Length.Should().Be(10);
    }

    [Fact]
    public void Constructor_ShouldAcceptBoundarySnippetLength()
    {
        var snippet = new string('a', 2000);
        var span = new SourceSpan(_sourceBlockId, 0, 100, snippet);

        span.SnippetText.Length.Should().Be(2000);
    }
}
