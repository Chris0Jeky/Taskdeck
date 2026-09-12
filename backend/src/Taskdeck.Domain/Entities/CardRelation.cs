using Taskdeck.Domain.Exceptions;
namespace Taskdeck.Domain.Entities;

public sealed record CardRelationEdge(Guid SourceCardId, Guid TargetCardId, string RelationType);
public sealed record CardRelationEndpoint(Guid CardId, Guid BoardId, bool IsArchived);

/// <summary>A canonical same-board link, separate from lifecycle, assignment and containment.</summary>
public sealed class CardRelation
{
    public Guid BoardId { get; private set; }
    public Guid SourceCardId { get; private set; }
    public Guid TargetCardId { get; private set; }
    public string RelationType { get; private set; } = string.Empty;
    private CardRelation() { }
    public CardRelation(Guid boardId, CardRelationEdge edge)
    {
        edge = CardRelationRules.Normalize(edge);
        BoardId = boardId;
        SourceCardId = edge.SourceCardId;
        TargetCardId = edge.TargetCardId;
        RelationType = edge.RelationType;
    }
    public CardRelationEdge ToEdge() => new(SourceCardId, TargetCardId, RelationType);
}

/// <summary>Pure rules shared by preview, execution and portability.</summary>
public static class CardRelationRules
{
    public const int MaximumRelations = 500;
    public static CardRelationEdge Normalize(CardRelationEdge edge)
    {
        if (edge is null || edge.SourceCardId == Guid.Empty || edge.TargetCardId == Guid.Empty || edge.SourceCardId == edge.TargetCardId)
            throw Invalid("Relations need two different cards.");
        return edge.RelationType switch
        {
            "depends-on" => new(edge.TargetCardId, edge.SourceCardId, "blocks"),
            "relates-to" when string.CompareOrdinal(edge.SourceCardId.ToString("D"), edge.TargetCardId.ToString("D")) > 0 =>
                new(edge.TargetCardId, edge.SourceCardId, "relates-to"),
            "relates-to" or "blocks" or "duplicates" or "spawned-from" => edge,
            _ => throw Invalid("Use relates-to, blocks, depends-on, duplicates, or spawned-from.")
        };
    }
    public static IReadOnlyList<CardRelationEdge> Validate(IReadOnlyList<CardRelationEdge> edges)
    {
        if (edges is null || edges.Count > MaximumRelations) throw Invalid("Use at most 500 relations per board.");
        var canonical = edges.Select(Normalize).ToArray();
        if (canonical.Distinct().Count() != canonical.Length) throw Invalid("A relation must not repeat.");
        foreach (var kind in new[] { "blocks", "duplicates", "spawned-from" })
        {
            var directed = canonical.Where(e => e.RelationType == kind).ToArray();
            var nodes = directed.SelectMany(e => new[] { e.SourceCardId, e.TargetCardId }).Distinct().ToArray();
            var counts = nodes.ToDictionary(id => id, _ => 0);
            var outgoing = directed.ToLookup(e => e.SourceCardId, e => e.TargetCardId);
            foreach (var edge in directed) counts[edge.TargetCardId]++;
            var queue = new Queue<Guid>(nodes.Where(id => counts[id] == 0));
            var visited = 0;
            while (queue.TryDequeue(out var id))
            {
                visited++;
                foreach (var target in outgoing[id]) if (--counts[target] == 0) queue.Enqueue(target);
            }
            if (visited != nodes.Length) throw Invalid($"This {kind} relation would create a cycle.");
        }
        return canonical;
    }
    public static IReadOnlyList<CardRelationEdge> Validate(Guid boardId, IReadOnlyList<CardRelationEdge> edges,
        IReadOnlyCollection<CardRelationEndpoint> endpoints, bool requireActive = false)
    {
        var canonical = Validate(edges);
        var valid = endpoints.Where(e => e.BoardId == boardId && (!requireActive || !e.IsArchived)).Select(e => e.CardId).ToHashSet();
        if (canonical.Any(e => !valid.Contains(e.SourceCardId) || !valid.Contains(e.TargetCardId)))
            throw Invalid(requireActive ? "Every relation must reference two active cards on this board." : "Every relation must reference two existing cards on this board.");
        return canonical;
    }
    public static IReadOnlyList<CardRelationEdge> Apply(Guid boardId, IReadOnlyList<CardRelationEdge> current,
        CardRelationEdge request, bool remove, IReadOnlyCollection<CardRelationEndpoint> endpoints)
    {
        var edge = Validate(boardId, [request], endpoints, requireActive: true)[0];
        if (remove && !current.Contains(edge)) throw Invalid("The relation does not exist.");
        return Validate(boardId, remove ? current.Where(e => e != edge).ToArray() : [.. current, edge], endpoints);
    }
    private static DomainException Invalid(string message) => new(ErrorCodes.ValidationError, message);
}
