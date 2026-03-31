using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class SearchService : ISearchService
{
    private const int MaxBoardResults = 10;
    private const int MaxCardResultsCeiling = 100;

    private readonly IUnitOfWork _unitOfWork;

    public SearchService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<GlobalSearchResultDto>> SearchAsync(
        Guid userId,
        string query,
        int maxResults = 20,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<GlobalSearchResultDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        // Clamp maxResults to a sane range
        var effectiveMaxResults = Math.Clamp(maxResults, 1, MaxCardResultsCeiling);
        var effectiveOffset = Math.Max(offset, 0);

        if (string.IsNullOrWhiteSpace(query))
            return Result.Success(new GlobalSearchResultDto([], [], 0, false, effectiveOffset, effectiveMaxResults));

        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length < 2)
            return Result.Success(new GlobalSearchResultDto([], [], 0, false, effectiveOffset, effectiveMaxResults));

        // Get boards the user can read
        var readableBoards = (await _unitOfWork.Boards.GetReadableByUserIdAsync(
            userId,
            includeArchived: false,
            cancellationToken)).ToList();

        // Search boards by name/description (not paginated — small fixed set)
        var matchingBoards = readableBoards
            .Where(b =>
                b.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                (b.Description != null && b.Description.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxBoardResults)
            .Select(b => new SearchBoardHitDto(b.Id, b.Name, b.Description, b.IsArchived))
            .ToList();

        // Search cards across all readable boards with offset pagination
        var readableBoardIds = readableBoards.Select(b => b.Id).ToList();

        var totalCardCount = await _unitOfWork.Cards.CountSearchAcrossBoardsAsync(
            readableBoardIds,
            trimmedQuery,
            cancellationToken);

        var matchingCards = (await _unitOfWork.Cards.SearchAcrossBoardsAsync(
            readableBoardIds,
            trimmedQuery,
            effectiveMaxResults,
            effectiveOffset,
            cancellationToken))
            .Select(c => new SearchCardHitDto(
                c.Id,
                c.BoardId,
                c.Board?.Name ?? "Unknown",
                c.ColumnId,
                c.Column?.Name ?? "Unknown",
                c.Title,
                c.Description))
            .ToList();

        var hasMore = effectiveOffset + matchingCards.Count < totalCardCount;

        return Result.Success(new GlobalSearchResultDto(
            matchingBoards, matchingCards, totalCardCount, hasMore, effectiveOffset, effectiveMaxResults));
    }
}
