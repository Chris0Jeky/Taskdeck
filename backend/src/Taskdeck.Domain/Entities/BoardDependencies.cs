namespace Taskdeck.Domain.Entities;

public sealed record CardDependency(Guid CardId, Guid DependsOnCardId);

/// <summary>Revision header for canonical relation rows, including an empty graph.</summary>
public sealed class BoardDependencies
{
    public Guid BoardId { get; private set; }
    public long Revision { get; private set; }
    public List<CardRelation> Relations { get; private set; } = [];
    private BoardDependencies() { }
    public BoardDependencies(Guid boardId) { BoardId = boardId; }
    public IReadOnlyList<CardRelationEdge> ReadRelations() => Relations.Select(e => e.ToEdge()).ToArray();
    public IReadOnlyList<CardDependency> ReadEdges() => Relations.Where(e => e.RelationType == "blocks")
        .Select(e => new CardDependency(e.TargetCardId, e.SourceCardId)).ToArray();
    public void InvalidateProjection() => Revision++;
    public void ReplaceRelations(IReadOnlyList<CardRelationEdge> edges)
    {
        var canonical = CardRelationRules.Validate(edges);
        var existing = Relations.ToDictionary(e => e.ToEdge());
        Relations = canonical.Select(e => existing.TryGetValue(e, out var row) ? row : new CardRelation(BoardId, e)).ToList();
        Revision++;
    }
    /// <summary>Legacy full dependency replacement retains all nondependency kinds.</summary>
    public void Replace(IReadOnlyList<CardDependency> edges)
    {
        if (edges is null) { CardRelationRules.Validate(null!); return; }
        ReplaceRelations([.. edges.Select(e => e is null ? null! : new CardRelationEdge(e.DependsOnCardId, e.CardId, "blocks")),
            .. ReadRelations().Where(e => e.RelationType != "blocks")]);
    }
}
