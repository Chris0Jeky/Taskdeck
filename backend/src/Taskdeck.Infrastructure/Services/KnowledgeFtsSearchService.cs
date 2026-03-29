using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Services;

public class KnowledgeFtsSearchService : IKnowledgeSearchService
{
    private readonly TaskdeckDbContext _context;

    public KnowledgeFtsSearchService(TaskdeckDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<KnowledgeSearchResultDto>> SearchAsync(
        string query,
        Guid userId,
        Guid? boardId = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        // Sanitize the query for FTS5 — remove special characters that could cause syntax errors
        var sanitizedQuery = SanitizeFtsQuery(query);
        if (string.IsNullOrWhiteSpace(sanitizedQuery))
            return Enumerable.Empty<KnowledgeSearchResultDto>();

        var sql = @"
            SELECT
                d.Id AS DocumentId,
                d.Title,
                snippet(KnowledgeDocumentsFts, 1, '>>>', '<<<', '...', 32) AS Snippet,
                rank AS Rank,
                d.BoardId,
                d.SourceType,
                d.Tags,
                d.CreatedAt
            FROM KnowledgeDocumentsFts fts
            JOIN KnowledgeDocuments d ON fts.document_id = d.Id
            WHERE KnowledgeDocumentsFts MATCH {0}
              AND d.UserId = {1}
              AND d.IsArchived = 0";

        if (boardId.HasValue)
        {
            sql += " AND d.BoardId = {2}";
            sql += " ORDER BY rank LIMIT {3}";

            var resultsWithBoard = await _context.Database
                .SqlQueryRaw<KnowledgeSearchRow>(sql, sanitizedQuery, userId.ToString().ToUpperInvariant(), boardId.Value.ToString().ToUpperInvariant(), limit)
                .ToListAsync(cancellationToken);

            return resultsWithBoard.Select(MapRowToDto).Where(r => r.DocumentId != Guid.Empty);
        }

        sql += " ORDER BY rank LIMIT {2}";

        var results = await _context.Database
            .SqlQueryRaw<KnowledgeSearchRow>(sql, sanitizedQuery, userId.ToString().ToUpperInvariant(), limit)
            .ToListAsync(cancellationToken);

        return results.Select(MapRowToDto).Where(r => r.DocumentId != Guid.Empty);
    }

    public async Task UpdateFtsIndexAsync(
        Guid documentId,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        // Delete old FTS entry and insert new one
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM KnowledgeDocumentsFts WHERE document_id = {0}",
            new object[] { documentId.ToString().ToUpperInvariant() },
            cancellationToken);

        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO KnowledgeDocumentsFts(title, content, document_id) VALUES ({0}, {1}, {2})",
            new object[] { title, content, documentId.ToString().ToUpperInvariant() },
            cancellationToken);
    }

    public async Task DeleteFtsIndexAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM KnowledgeDocumentsFts WHERE document_id = {0}",
            new object[] { documentId.ToString().ToUpperInvariant() },
            cancellationToken);
    }

    internal static string SanitizeFtsQuery(string query)
    {
        // Remove FTS5 special operators and problematic characters
        var sanitized = query
            .Replace("\"", " ")
            .Replace("'", " ")
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace("*", " ")
            .Replace("-", " ")
            .Replace("+", " ")
            .Replace(":", " ")
            .Replace("^", " ")
            .Replace("{", " ")
            .Replace("}", " ");

        // Split into words and rejoin — removes extra whitespace
        var words = sanitized.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);

        return string.Join(" ", words);
    }

    private static KnowledgeSearchResultDto MapRowToDto(KnowledgeSearchRow row)
    {
        Guid? boardId = null;
        if (!string.IsNullOrEmpty(row.BoardId) && Guid.TryParse(row.BoardId, out var parsedBoardId))
            boardId = parsedBoardId;

        var sourceType = Enum.TryParse<KnowledgeSourceType>(row.SourceType?.ToString(), out var parsed)
            ? parsed
            : KnowledgeSourceType.Manual;

        var documentId = Guid.TryParse(row.DocumentId, out var parsedDocId)
            ? parsedDocId
            : Guid.Empty;

        var createdAt = DateTimeOffset.TryParse(row.CreatedAt, out var parsedDate)
            ? parsedDate
            : DateTimeOffset.MinValue;

        return new KnowledgeSearchResultDto(
            documentId,
            row.Title,
            row.Snippet,
            row.Rank,
            boardId,
            sourceType,
            row.Tags,
            createdAt);
    }
}

// Internal class to hold raw SQL query results
internal class KnowledgeSearchRow
{
    public string DocumentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public double Rank { get; set; }
    public string? BoardId { get; set; }
    public string? SourceType { get; set; }
    public string? Tags { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
