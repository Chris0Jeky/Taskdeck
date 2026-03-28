using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record KnowledgeDocumentDto(
    Guid Id,
    Guid UserId,
    Guid? BoardId,
    string Title,
    string Content,
    KnowledgeSourceType SourceType,
    string? SourceUrl,
    string? Tags,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateKnowledgeDocumentDto(
    string Title,
    string Content,
    KnowledgeSourceType SourceType,
    Guid? BoardId = null,
    string? SourceUrl = null,
    string? Tags = null
);

public record UpdateKnowledgeDocumentDto(
    string Title,
    string Content,
    string? Tags = null
);

public record KnowledgeSearchResultDto(
    Guid DocumentId,
    string Title,
    string Snippet,
    double Rank,
    Guid? BoardId,
    KnowledgeSourceType SourceType,
    string? Tags,
    DateTimeOffset CreatedAt
);
