using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>Type-agnostic parent graph, including archived descendants. Maximum three links.</summary>
public static class CardHierarchy
{
    public static void Validate(IEnumerable<Card> cards) => ValidateCore(cards, null, null);

    public static void ValidateParent(IEnumerable<Card> cards, Guid cardId, Guid? parentId)
        => ValidateCore(cards, cardId, parentId);

    private static void ValidateCore(IEnumerable<Card> cards, Guid? changedId, Guid? parentId)
    {
        var graph = cards.ToDictionary(card => card.Id);
        if (changedId.HasValue && !graph.ContainsKey(changedId.Value))
            throw new DomainException(ErrorCodes.ValidationError, "Card is missing from the parent graph.");
        foreach (var start in graph.Values)
        {
            var visited = new HashSet<Guid> { start.Id };
            var current = start;
            var depth = 0;
            while ((current.Id == changedId ? parentId : current.ParentCardId) is Guid nextId)
            {
                if (!graph.TryGetValue(nextId, out var next) || next.BoardId != start.BoardId)
                    throw new DomainException(ErrorCodes.ValidationError, "Parent must be an existing card on the same board.");
                if (!visited.Add(nextId))
                    throw new DomainException(ErrorCodes.ValidationError, "Card parents cannot form a cycle.");
                if (++depth > 3)
                    throw new DomainException(ErrorCodes.ValidationError, "Card hierarchy supports at most three links (four levels), including descendants.");
                current = next;
            }
        }
    }
}
