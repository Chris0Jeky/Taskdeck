using System.Security.Cryptography;
using System.Text;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public partial class CardService
{
    public async Task<Result<CardDetachPreviewDto>> PreviewDetachAsync(Guid boardId, Guid cardId, CancellationToken ct = default)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(cardId, ct);
        if (card is null || card.BoardId != boardId)
            return Result.Failure<CardDetachPreviewDto>(ErrorCodes.NotFound, "Card not found in this board");
        var children = await ReadDetachChildrenAsync(card, ct);
        return Result.Success(BuildDetachPreview(card, children));
    }

    internal static CardDetachPreviewDto BuildDetachPreview(Card card, IReadOnlyList<Card> children)
        => new(card.Id, card.UpdatedAt, ChildrenFingerprint(children), children.OrderBy(child => child.Id)
            .Select(child => new CardDetachChildDto(child.Id, child.ParentCardId, child.Title, child.IsArchived, child.UpdatedAt)).ToArray());

    internal static string ChildrenFingerprint(IEnumerable<Card> children)
    {
        var canonical = "card-children-v1\n" + string.Join("\n", children.OrderBy(child => child.Id)
            .Select(child => $"{child.Id:D}|{child.ParentCardId:D}|{child.UpdatedAt.UtcTicks}"));
        return "v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static Result ValidateDetachConfirmation(Card card, IReadOnlyList<Card> children, CardLifecycleDto? confirmation)
    {
        if (confirmation?.ExpectedUpdatedAt is DateTimeOffset expected && card.UpdatedAt != expected)
            return Result.Failure(ErrorCodes.Conflict, "Card changed since preview. Refresh and confirm again.");
        if (children.Count > 0 && (confirmation?.ExpectedUpdatedAt is null || confirmation.ExpectedChildrenFingerprint is null))
            return Result.Failure(ErrorCodes.Conflict, "Preview and confirm every child detachment before archiving or deleting this parent.");
        if (confirmation?.ExpectedChildrenFingerprint is string fingerprint && fingerprint != ChildrenFingerprint(children))
            return Result.Failure(ErrorCodes.Conflict, "Children changed since preview. Refresh and confirm the full child list again.");
        return Result.Success();
    }

    private async Task<IReadOnlyList<Card>> ReadDetachChildrenAsync(Card card, CancellationToken ct)
        => (await _unitOfWork.Cards.GetHierarchyByBoardIdAsync(card.BoardId, ct))
            .Where(child => child.ParentCardId == card.Id).OrderBy(child => child.Id).ToArray();

    private async Task StageDetachChildrenAsync(Card card, IReadOnlyList<Card> children, Guid? actor, CancellationToken ct)
    {
        foreach (var child in children)
        {
            child.DetachParent();
            await _unitOfWork.AuditLogs.AddAsync(new AuditLog("card", child.Id, AuditAction.Updated, actor,
                $"ParentCardId: {card.Id} -> none; parent archived or deleted"), ct);
        }
    }

    /// <summary>
    /// Publishes the "child detached" events for an archive or delete. <paramref name="sink"/>
    /// is non-null only on the proposal apply lane, where these events must wait for the
    /// executor's outer transaction to commit (#2934); everywhere else the service's own
    /// notifier publishes them immediately.
    /// </summary>
    private async Task NotifyDetachedChildrenAsync(Guid boardId, IReadOnlyList<Card> children, CancellationToken ct,
        IBoardRealtimeNotifier? sink = null)
    {
        var notifier = sink ?? _realtimeNotifier;
        foreach (var child in children)
            await notifier.NotifyBoardMutationAsync(new BoardRealtimeEvent(
                boardId, "card", "updated", child.Id, DateTimeOffset.UtcNow), ct);
    }

    private static void ValidateActiveParent(IEnumerable<Card> graph, Guid? parentId)
    {
        if (parentId is Guid id && graph.FirstOrDefault(card => card.Id == id)?.IsArchived == true)
            throw new DomainException(ErrorCodes.ValidationError, "Restore the parent card before assigning it.");
    }
}
