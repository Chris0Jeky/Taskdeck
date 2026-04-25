using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class EvidenceLinkTests
{
    private readonly Guid _fieldId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateLink_WithRequiredFields()
    {
        var link = new EvidenceLink("InboxCapture", "cap-123", _fieldId);

        link.SourceType.Should().Be("InboxCapture");
        link.SourceId.Should().Be("cap-123");
        link.ProvenanceFieldId.Should().Be(_fieldId);
        link.Label.Should().BeNull();
        link.SpanStart.Should().BeNull();
        link.SpanEnd.Should().BeNull();
        link.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_ShouldCreateLink_WithAllOptionalFields()
    {
        var link = new EvidenceLink(
            "ChatMessage",
            "msg-456",
            _fieldId,
            label: "User's original message",
            spanStart: 10,
            spanEnd: 50);

        link.Label.Should().Be("User's original message");
        link.SpanStart.Should().Be(10);
        link.SpanEnd.Should().Be(50);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSourceTypeIsEmpty()
    {
        var act = () => new EvidenceLink("", "cap-123", _fieldId);

        act.Should().Throw<DomainException>()
            .WithMessage("SourceType cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSourceTypeExceedsMaxLength()
    {
        var longType = new string('x', 101);

        var act = () => new EvidenceLink(longType, "cap-123", _fieldId);

        act.Should().Throw<DomainException>()
            .WithMessage("SourceType cannot exceed 100 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSourceIdIsEmpty()
    {
        var act = () => new EvidenceLink("InboxCapture", "", _fieldId);

        act.Should().Throw<DomainException>()
            .WithMessage("SourceId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSourceIdExceedsMaxLength()
    {
        var longId = new string('i', 501);

        var act = () => new EvidenceLink("InboxCapture", longId, _fieldId);

        act.Should().Throw<DomainException>()
            .WithMessage("SourceId cannot exceed 500 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFieldIdIsEmpty()
    {
        var act = () => new EvidenceLink("InboxCapture", "cap-123", Guid.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("ProvenanceFieldId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLabelExceedsMaxLength()
    {
        var longLabel = new string('l', 201);

        var act = () => new EvidenceLink("InboxCapture", "cap-123", _fieldId, label: longLabel);

        act.Should().Throw<DomainException>()
            .WithMessage("Label cannot exceed 200 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSpanStartIsNegative()
    {
        var act = () => new EvidenceLink("InboxCapture", "cap-123", _fieldId, spanStart: -1);

        act.Should().Throw<DomainException>()
            .WithMessage("SpanStart cannot be negative");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSpanEndIsNegative()
    {
        var act = () => new EvidenceLink("InboxCapture", "cap-123", _fieldId, spanEnd: -1);

        act.Should().Throw<DomainException>()
            .WithMessage("SpanEnd cannot be negative");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSpanEndIsLessThanSpanStart()
    {
        var act = () => new EvidenceLink("InboxCapture", "cap-123", _fieldId, spanStart: 20, spanEnd: 10);

        act.Should().Throw<DomainException>()
            .WithMessage("SpanEnd cannot be less than SpanStart");
    }

    [Fact]
    public void Constructor_ShouldAccept_WhenSpanStartEqualsSpanEnd()
    {
        var link = new EvidenceLink("InboxCapture", "cap-123", _fieldId, spanStart: 5, spanEnd: 5);

        link.SpanStart.Should().Be(5);
        link.SpanEnd.Should().Be(5);
    }

    [Fact]
    public void Constructor_ShouldAccept_WhenOnlySpanStartProvided()
    {
        var link = new EvidenceLink("InboxCapture", "cap-123", _fieldId, spanStart: 10);

        link.SpanStart.Should().Be(10);
        link.SpanEnd.Should().BeNull();
    }
}
