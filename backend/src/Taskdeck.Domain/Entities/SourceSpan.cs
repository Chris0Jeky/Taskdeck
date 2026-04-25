using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A character-level span within a <see cref="SourceBlock"/> that identifies a
/// precise range of text. Used by <see cref="EvidenceLink"/> to point at the
/// exact evidence supporting an <see cref="IntentCandidate"/>.
/// </summary>
public class SourceSpan : Entity
{
    public Guid SourceBlockId { get; private set; }
    public int StartOffset { get; private set; }
    public int EndOffset { get; private set; }

    /// <summary>
    /// Verbatim text captured from the span. Stored separately from the
    /// offsets so the evidence remains readable even if the source is
    /// truncated or compacted later.
    /// </summary>
    public string SnippetText { get; private set; } = string.Empty;

    // Navigation
    public SourceBlock SourceBlock { get; private set; } = null!;

    private SourceSpan() { } // EF Core

    public SourceSpan(
        Guid sourceBlockId,
        int startOffset,
        int endOffset,
        string snippetText)
    {
        if (sourceBlockId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "SourceBlockId cannot be empty");
        if (startOffset < 0)
            throw new DomainException(ErrorCodes.ValidationError, "StartOffset must be non-negative");
        if (endOffset < 0)
            throw new DomainException(ErrorCodes.ValidationError, "EndOffset must be non-negative");
        if (endOffset <= startOffset)
            throw new DomainException(ErrorCodes.ValidationError, "EndOffset must be greater than StartOffset");
        if (string.IsNullOrEmpty(snippetText))
            throw new DomainException(ErrorCodes.ValidationError, "SnippetText cannot be empty");
        if (snippetText.Length > 2000)
            throw new DomainException(ErrorCodes.ValidationError, "SnippetText cannot exceed 2000 characters");

        SourceBlockId = sourceBlockId;
        StartOffset = startOffset;
        EndOffset = endOffset;
        SnippetText = snippetText;
    }

    /// <summary>Length of the span in characters.</summary>
    public int Length => EndOffset - StartOffset;
}
