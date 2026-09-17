using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>Bounds retained relation DTOs, not total account-export bytes or database read time.</summary>
internal static class BufferedCardRelationExport
{
    internal const int MaxRows = 10_000;

    internal static async Task<IReadOnlyList<UserDataExportCardRelationDto>> ReadAsync(
        IReadOnlyDictionary<Guid, HashSet<Guid>> exportedCardIdsByBoard,
        IAsyncEnumerable<UserDataExportCardRelationDto> source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (exportedCardIdsByBoard.Count == 0)
            return [];

        var result = new List<UserDataExportCardRelationDto>();
        await foreach (var relation in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The page query checks current account access. Also retain the buffered export's
            // exact card scope: concurrent additions must not create dangling exported edges.
            if (relation.BoardId == Guid.Empty ||
                !exportedCardIdsByBoard.TryGetValue(relation.BoardId, out var cardIds) ||
                !cardIds.Contains(relation.SourceCardId) || !cardIds.Contains(relation.TargetCardId))
                continue;

            if (result.Count >= MaxRows)
                throw new DomainException(ErrorCodes.PayloadTooLarge,
                    "Too many card relations to buffer; use the streaming export endpoint.");
            result.Add(relation);
        }
        return result;
    }
}
