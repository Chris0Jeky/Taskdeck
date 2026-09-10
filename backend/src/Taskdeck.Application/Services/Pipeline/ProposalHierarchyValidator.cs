using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Pipeline;

public static class ProposalHierarchyValidator
{
    public static bool AffectsHierarchy(string action, string target, JsonElement parameters)
        => target.Equals("card", StringComparison.OrdinalIgnoreCase) &&
            (action.ToLowerInvariant() is "archive-lifecycle" or "restore-lifecycle" or "delete" ||
             parameters.TryGetProperty("parentCardId", out _) || parameters.TryGetProperty("clearParent", out _));

    public static bool TryReadParent(JsonElement parameters, out Guid? parent, out bool clear, out string error)
    {
        parent = null;
        error = "";
        if (!OperationParameterParser.TryGetOptionalBoolean(parameters, "clearParent", out _, out clear, out error)) return false;
        if (parameters.TryGetProperty("parentCardId", out var value))
        {
            if (value.ValueKind != JsonValueKind.String || !value.TryGetGuid(out var id) || id == Guid.Empty)
            { error = "parentCardId must be a non-empty UUID. Use clearParent to remove a parent."; return false; }
            parent = id;
        }
        if (parent.HasValue && clear) { error = "parentCardId and clearParent cannot both be set."; return false; }
        return true;
    }

    // Descriptions are produced from the exact complete snapshot whose pins and graph were validated.
    // They must never be reconstructed by the best-effort display-name lookup path.
    public static async Task<Result<Dictionary<int, string>>> ValidateAsync(IUnitOfWork uow, Guid? boardId,
        IEnumerable<ProposalOperationDto> operations, CancellationToken ct)
    {
        var descriptions = new Dictionary<int, string>();
        var materialized = operations.ToList();
        var hierarchy = new List<(ProposalOperationDto Operation, JsonElement Parameters)>();
        foreach (var op in materialized)
        {
            if (!OperationParameterParser.TryDeserializeParameters(op.Parameters, out var parameters, out var error))
                return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, error);
            if (AffectsHierarchy(op.ActionType, op.TargetType, parameters)) hierarchy.Add((op, parameters));
        }
        if (hierarchy.Count == 0) return Result.Success(descriptions);
        if (hierarchy.Count > 1)
            return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, "Use a separate proposal for each hierarchy-affecting operation on a board.");
        if (boardId is not Guid id || await uow.Boards.GetByIdAsync(id, ct) is not Board board)
            return Result.Failure<Dictionary<int, string>>(ErrorCodes.NotFound, "Board not found");
        if (board.IsArchived)
            return Result.Failure<Dictionary<int, string>>(ErrorCodes.InvalidOperation, "Restore the board before changing its hierarchy.");
        var graph = await uow.Cards.GetHierarchyByBoardIdAsync(id, ct);
        foreach (var (op, parameters) in hierarchy)
        {
            if (!TryReadParent(parameters, out var parentId, out var clear, out var error))
                return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, error);
            var action = op.ActionType.ToLowerInvariant();
            var create = action == "create";
            if ((parentId.HasValue || clear) && action is not ("create" or "update"))
                return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, "Parent fields require a create or update card operation.");
            if (create && clear)
                return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, "A new card has no parent to remove.");
            Card? card;
            if (create)
            {
                var cardId = Guid.TryParse(op.TargetId, out var proposedId) ? proposedId : Guid.NewGuid();
                if (cardId == Guid.Empty || graph.Any(existing => existing.Id == cardId))
                    return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, "Create card id must be new and non-empty.");
                card = new Card(cardId, id, Guid.NewGuid(), "Proposed card");
            }
            else
            {
                if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out error))
                    return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, error);
                card = graph.FirstOrDefault(candidate => candidate.Id == cardId);
                if (card is null) return Result.Failure<Dictionary<int, string>>(ErrorCodes.NotFound, "Card not found in this board");
                if (!parameters.TryGetProperty("expectedUpdatedAt", out var stamp) || stamp.ValueKind != JsonValueKind.String || !stamp.TryGetDateTimeOffset(out var expected))
                    return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, "expectedUpdatedAt is required for hierarchy changes.");
                if (expected != card.UpdatedAt)
                    return Result.Failure<Dictionary<int, string>>(ErrorCodes.Conflict, "Card changed since this proposal was prepared. Prepare a new proposal.");
            }
            if (action is "archive-lifecycle" or "delete")
            {
                var children = graph.Where(child => child.ParentCardId == card.Id).OrderBy(child => child.Id).ToArray();
                var affected = children.Select(child => child.Id).Append(card.Id).ToHashSet();
                foreach (var other in materialized.Where(other => other.Sequence != op.Sequence && other.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase)))
                    if (OperationParameterParser.TryDeserializeParameters(other.Parameters, out var otherParameters, out _) &&
                        OperationParameterParser.TryGetRequiredGuid(otherParameters, "cardId", out var otherId, out _) && affected.Contains(otherId))
                        return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, "A parent detachment proposal cannot also edit that parent or its affected children.");
                if (parameters.TryGetProperty("expectedChildrenFingerprint", out var fp) && fp.ValueKind != JsonValueKind.String)
                    return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, "expectedChildrenFingerprint must be a string.");
                var confirmed = CardService.ValidateDetachConfirmation(card, children,
                    new CardLifecycleDto(card.UpdatedAt, OperationParameterParser.GetOptionalString(parameters, "expectedChildrenFingerprint")));
                if (!confirmed.IsSuccess) return Result.Failure<Dictionary<int, string>>(confirmed.ErrorCode, confirmed.ErrorMessage);
                descriptions[op.Sequence] = $"{(action == "delete" ? "Delete" : "Archive")} card '{card.Title}' ({card.Id}). " +
                    (children.Length == 0 ? "No children to detach." : "Detach every direct child (preserve IDs, columns and history):\n" + string.Join("\n", children.Select(child =>
                        $"  - '{child.Title}' ({child.Id}){(child.IsArchived ? " [archived]" : "")}: ParentCardId {card.Id} -> none; version {child.UpdatedAt:O}")));
            }
            else if (parentId.HasValue || clear)
            {
                if (!create && card.IsArchived)
                    return Result.Failure<Dictionary<int, string>>(ErrorCodes.InvalidOperation, "Restore the card before changing its parent.");
                if (parentId.HasValue && graph.FirstOrDefault(candidate => candidate.Id == parentId)?.IsArchived == true)
                    return Result.Failure<Dictionary<int, string>>(ErrorCodes.ValidationError, "Restore the parent card before assigning it.");
                try { CardHierarchy.ValidateParent(create ? graph.Append(card) : graph, card.Id, parentId); }
                catch (DomainException ex) { return Result.Failure<Dictionary<int, string>>(ex.ErrorCode, ex.Message); }
                string DescribeParent(Guid? value) => value is Guid parent
                    ? $"'{graph.First(candidate => candidate.Id == parent).Title}' ({parent})" : "none";
                var title = create ? OperationParameterParser.GetOptionalString(parameters, "title") ?? "New card" : card.Title;
                var identity = create && string.IsNullOrWhiteSpace(op.TargetId) ? "new card" : card.Id.ToString();
                descriptions[op.Sequence] = $"Parent of '{title}' ({identity}): {DescribeParent(card.ParentCardId)} -> {DescribeParent(parentId)}";
            }
        }
        return Result.Success(descriptions);
    }
}
