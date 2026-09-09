using System.Text.Json;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public sealed record CardDependency(Guid CardId, Guid DependsOnCardId);

/// <summary>Explicit prerequisite relationships. They never change card status or deadlines.</summary>
public sealed class BoardDependencies
{
    public Guid BoardId { get; private set; }
    public long Revision { get; private set; }
    public string EdgesJson { get; private set; } = "[]";
    private BoardDependencies() { }
    public BoardDependencies(Guid boardId) { BoardId = boardId; }
    public IReadOnlyList<CardDependency> ReadEdges() => JsonSerializer.Deserialize<List<CardDependency>>(EdgesJson)!;

    public void Replace(IReadOnlyList<CardDependency> edges)
    {
        if (edges is null || edges.Count > 500)
            throw new DomainException(ErrorCodes.ValidationError, "Use at most 500 dependencies per board.");
        var seen = new HashSet<CardDependency>();
        foreach (var edge in edges)
            if (edge is null || edge.CardId == Guid.Empty || edge.DependsOnCardId == Guid.Empty ||
                edge.CardId == edge.DependsOnCardId || !seen.Add(edge))
                throw new DomainException(ErrorCodes.ValidationError, "Dependencies need two different cards and must not repeat.");
        // Kahn's algorithm is bounded by the supplied edges, including long chains.
        var nodes = edges.SelectMany(e => new[] { e.CardId, e.DependsOnCardId }).Distinct().ToArray();
        var counts = nodes.ToDictionary(id => id, _ => 0);
        var outgoing = edges.ToLookup(e => e.CardId, e => e.DependsOnCardId);
        foreach (var edge in edges) counts[edge.DependsOnCardId]++;
        var queue = new Queue<Guid>(nodes.Where(id => counts[id] == 0));
        var visited = 0;
        while (queue.TryDequeue(out var id))
        {
            visited++;
            foreach (var target in outgoing[id]) if (--counts[target] == 0) queue.Enqueue(target);
        }
        if (visited != nodes.Length)
            throw new DomainException(ErrorCodes.ValidationError, "This dependency would create a cycle. Choose a different prerequisite.");
        EdgesJson = JsonSerializer.Serialize(edges);
        Revision++;
    }
}
