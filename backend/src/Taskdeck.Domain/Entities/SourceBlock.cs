using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A discrete text block from a source (capture input, chat message, etc.)
/// with position metadata. Blocks are contained within an
/// <see cref="IntentEnvelopeV1"/> and may contain multiple
/// <see cref="SourceSpan"/> ranges used as evidence.
/// </summary>
public class SourceBlock : Entity
{
    public Guid EnvelopeId { get; private set; }

    /// <summary>
    /// Zero-based index of this block within the envelope's ordered list.
    /// </summary>
    public int Position { get; private set; }

    /// <summary>
    /// The raw text content of this block.
    /// </summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>
    /// Identifies where this block came from, e.g. "capture", "chat", "paste".
    /// </summary>
    public string SourceType { get; private set; } = string.Empty;

    /// <summary>
    /// Optional reference to the originating entity (capture item ID, chat
    /// message ID, etc.). Not a foreign key -- just a correlation hint.
    /// </summary>
    public string? SourceReferenceId { get; private set; }

    private readonly List<SourceSpan> _spans = new();
    public IReadOnlyList<SourceSpan> Spans => _spans.AsReadOnly();

    // Navigation
    public IntentEnvelopeV1 Envelope { get; private set; } = null!;

    private SourceBlock() { } // EF Core

    public SourceBlock(
        Guid envelopeId,
        int position,
        string content,
        string sourceType,
        string? sourceReferenceId = null)
    {
        if (envelopeId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "EnvelopeId cannot be empty");
        if (position < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Position must be non-negative");
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException(ErrorCodes.ValidationError, "Content cannot be empty");
        if (content.Length > 50_000)
            throw new DomainException(ErrorCodes.ValidationError, "Content cannot exceed 50000 characters");
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new DomainException(ErrorCodes.ValidationError, "SourceType cannot be empty");
        if (sourceType.Length > 50)
            throw new DomainException(ErrorCodes.ValidationError, "SourceType cannot exceed 50 characters");

        EnvelopeId = envelopeId;
        Position = position;
        Content = content;
        SourceType = sourceType;
        SourceReferenceId = string.IsNullOrWhiteSpace(sourceReferenceId) ? null : sourceReferenceId.Trim();
    }

    public SourceSpan AddSpan(int startOffset, int endOffset, string snippetText)
    {
        if (startOffset > Content.Length)
            throw new DomainException(ErrorCodes.ValidationError,
                $"StartOffset ({startOffset}) exceeds block content length ({Content.Length})");
        if (endOffset > Content.Length)
            throw new DomainException(ErrorCodes.ValidationError,
                $"EndOffset ({endOffset}) exceeds block content length ({Content.Length})");

        var span = new SourceSpan(Id, startOffset, endOffset, snippetText);
        _spans.Add(span);
        Touch();
        return span;
    }
}
