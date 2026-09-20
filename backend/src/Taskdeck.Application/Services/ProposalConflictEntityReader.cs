using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Per-review visibility boundary for referenced entities. A proposal is untrusted
/// input: access to its own row must never authorize a foreign card or column.
/// Null deliberately represents both missing and inaccessible entities so evidence
/// cannot distinguish them. Instantiate only after proposal-level authorization.
/// </summary>
internal sealed class ProposalConflictEntityReader(
    IUnitOfWork unitOfWork,
    IAuthorizationService authorization,
    Guid? proposalBoardId,
    Guid userId)
{
    private readonly Dictionary<Guid, Card?> _cards = [];
    private readonly Dictionary<Guid, Column?> _columns = [];
    private readonly Dictionary<Guid, bool> _readableBoards = [];

    public async Task<Card?> GetCardAsync(Guid cardId, CancellationToken cancellationToken)
    {
        if (_cards.TryGetValue(cardId, out var cached))
            return cached;

        var card = await unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken);
        var visible = card is not null && await CanReadEntityBoardAsync(card.BoardId) ? card : null;
        _cards[cardId] = visible;
        return visible;
    }

    public async Task<Column?> GetColumnAsync(Guid columnId, CancellationToken cancellationToken)
    {
        if (_columns.TryGetValue(columnId, out var cached))
            return cached;

        var column = await unitOfWork.Columns.GetByIdWithCardsAsync(columnId, cancellationToken);
        var visible = column is not null && await CanReadEntityBoardAsync(column.BoardId) ? column : null;
        _columns[columnId] = visible;
        return visible;
    }

    private async Task<bool> CanReadEntityBoardAsync(Guid entityBoardId)
    {
        // The detector has already checked current permission on this exact board.
        // Even access to some other board must not expand a scoped proposal's reads.
        if (proposalBoardId.HasValue)
            return entityBoardId == proposalBoardId.Value;

        if (_readableBoards.TryGetValue(entityBoardId, out var cached))
            return cached;

        // Boardless ownership grants access to the proposal, not to its targets.
        var permission = await authorization.CanReadBoardAsync(userId, entityBoardId);
        var canRead = permission.IsSuccess && permission.Value;
        _readableBoards[entityBoardId] = canRead;
        return canRead;
    }
}
