using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IKnowledgeService
{
    Task<Result<KnowledgeDocumentDto>> CreateDocumentAsync(
        Guid userId,
        CreateKnowledgeDocumentDto dto,
        CancellationToken cancellationToken = default);

    Task<Result<KnowledgeDocumentDto>> UpdateDocumentAsync(
        Guid userId,
        Guid documentId,
        UpdateKnowledgeDocumentDto dto,
        CancellationToken cancellationToken = default);

    Task<Result> ArchiveDocumentAsync(
        Guid userId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<Result<KnowledgeDocumentDto>> GetDocumentAsync(
        Guid userId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<KnowledgeDocumentDto>>> ListDocumentsAsync(
        Guid userId,
        Guid? boardId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<KnowledgeSearchResultDto>>> SearchDocumentsAsync(
        Guid userId,
        string query,
        Guid? boardId = null,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
