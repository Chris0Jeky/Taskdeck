using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class KnowledgeService : IKnowledgeService
{
    private const int ChunkSize = 1000;
    private const int MaxSearchLimit = 100;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IKnowledgeSearchService _searchService;

    public KnowledgeService(IUnitOfWork unitOfWork, IKnowledgeSearchService searchService)
    {
        _unitOfWork = unitOfWork;
        _searchService = searchService;
    }

    public async Task<Result<KnowledgeDocumentDto>> CreateDocumentAsync(
        Guid userId,
        CreateKnowledgeDocumentDto dto,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<KnowledgeDocumentDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        try
        {
            var document = new KnowledgeDocument(
                userId,
                dto.Title,
                dto.Content,
                dto.SourceType,
                dto.BoardId,
                dto.SourceUrl,
                dto.Tags);

            await _unitOfWork.KnowledgeDocuments.AddAsync(document, cancellationToken);

            var chunks = ChunkContent(document.Id, dto.Content);
            foreach (var chunk in chunks)
            {
                await _unitOfWork.KnowledgeChunks.AddAsync(chunk, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(document));
        }
        catch (DomainException ex)
        {
            return Result.Failure<KnowledgeDocumentDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<KnowledgeDocumentDto>> UpdateDocumentAsync(
        Guid userId,
        Guid documentId,
        UpdateKnowledgeDocumentDto dto,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<KnowledgeDocumentDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var document = await _unitOfWork.KnowledgeDocuments.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
            return Result.Failure<KnowledgeDocumentDto>(ErrorCodes.NotFound, "Knowledge document not found");

        if (document.UserId != userId)
            return Result.Failure<KnowledgeDocumentDto>(ErrorCodes.Forbidden, "You do not have access to this document");

        try
        {
            document.Update(dto.Title, dto.Content, dto.Tags);

            await _unitOfWork.KnowledgeChunks.DeleteByDocumentIdAsync(documentId, cancellationToken);

            var chunks = ChunkContent(document.Id, dto.Content);
            foreach (var chunk in chunks)
            {
                await _unitOfWork.KnowledgeChunks.AddAsync(chunk, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(document));
        }
        catch (DomainException ex)
        {
            return Result.Failure<KnowledgeDocumentDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result> ArchiveDocumentAsync(
        Guid userId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "User ID cannot be empty");

        var document = await _unitOfWork.KnowledgeDocuments.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
            return Result.Failure(ErrorCodes.NotFound, "Knowledge document not found");

        if (document.UserId != userId)
            return Result.Failure(ErrorCodes.Forbidden, "You do not have access to this document");

        document.Archive();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<KnowledgeDocumentDto>> GetDocumentAsync(
        Guid userId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<KnowledgeDocumentDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var document = await _unitOfWork.KnowledgeDocuments.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
            return Result.Failure<KnowledgeDocumentDto>(ErrorCodes.NotFound, "Knowledge document not found");

        if (document.UserId != userId)
            return Result.Failure<KnowledgeDocumentDto>(ErrorCodes.Forbidden, "You do not have access to this document");

        return Result.Success(MapToDto(document));
    }

    public async Task<Result<IEnumerable<KnowledgeDocumentDto>>> ListDocumentsAsync(
        Guid userId,
        Guid? boardId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<IEnumerable<KnowledgeDocumentDto>>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var documents = await _unitOfWork.KnowledgeDocuments.GetByUserIdAsync(
            userId, boardId, false, limit, offset, cancellationToken);

        return Result.Success(documents.Select(MapToDto));
    }

    public async Task<Result<IEnumerable<KnowledgeSearchResultDto>>> SearchDocumentsAsync(
        Guid userId,
        string query,
        Guid? boardId = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<IEnumerable<KnowledgeSearchResultDto>>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(query))
            return Result.Failure<IEnumerable<KnowledgeSearchResultDto>>(ErrorCodes.ValidationError, "Search query cannot be empty");

        if (limit <= 0 || limit > MaxSearchLimit)
            limit = 20;

        var results = await _searchService.SearchAsync(query, userId, boardId, limit, cancellationToken);
        return Result.Success(results);
    }

    internal static List<KnowledgeChunk> ChunkContent(Guid documentId, string content)
    {
        var chunks = new List<KnowledgeChunk>();

        // Split by paragraphs first
        var paragraphs = content.Split(
            new[] { "\r\n\r\n", "\n\n" },
            StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = string.Empty;
        var chunkIndex = 0;

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (currentChunk.Length + trimmed.Length + 2 > ChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(new KnowledgeChunk(documentId, chunkIndex, currentChunk.Trim()));
                chunkIndex++;
                currentChunk = string.Empty;
            }

            // If a single paragraph exceeds chunk size, split by character boundary
            if (trimmed.Length > ChunkSize)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(new KnowledgeChunk(documentId, chunkIndex, currentChunk.Trim()));
                    chunkIndex++;
                    currentChunk = string.Empty;
                }

                for (var i = 0; i < trimmed.Length; i += ChunkSize)
                {
                    var length = Math.Min(ChunkSize, trimmed.Length - i);
                    chunks.Add(new KnowledgeChunk(documentId, chunkIndex, trimmed.Substring(i, length)));
                    chunkIndex++;
                }
            }
            else
            {
                if (currentChunk.Length > 0)
                    currentChunk += "\n\n";
                currentChunk += trimmed;
            }
        }

        if (currentChunk.Trim().Length > 0)
        {
            chunks.Add(new KnowledgeChunk(documentId, chunkIndex, currentChunk.Trim()));
        }

        return chunks;
    }

    private static KnowledgeDocumentDto MapToDto(KnowledgeDocument document)
    {
        return new KnowledgeDocumentDto(
            document.Id,
            document.UserId,
            document.BoardId,
            document.Title,
            document.Content,
            document.SourceType,
            document.SourceUrl,
            document.Tags,
            document.IsArchived,
            document.CreatedAt,
            document.UpdatedAt);
    }
}
