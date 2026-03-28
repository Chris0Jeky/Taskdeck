using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class KnowledgeChunk : Entity
{
    private const int MaxMetadataLength = 4000;

    public Guid DocumentId { get; private set; }
    public int ChunkIndex { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? Metadata { get; private set; }

    private KnowledgeChunk() : base()
    {
    }

    public KnowledgeChunk(
        Guid documentId,
        int chunkIndex,
        string content,
        string? metadata = null)
        : base()
    {
        if (documentId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Document ID cannot be empty");

        if (chunkIndex < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Chunk index must be non-negative");

        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException(ErrorCodes.ValidationError, "Chunk content cannot be empty");

        if (metadata is not null && metadata.Length > MaxMetadataLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Metadata cannot exceed {MaxMetadataLength} characters");

        DocumentId = documentId;
        ChunkIndex = chunkIndex;
        Content = content;
        Metadata = metadata;
    }
}
