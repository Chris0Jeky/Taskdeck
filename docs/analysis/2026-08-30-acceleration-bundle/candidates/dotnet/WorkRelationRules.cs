using System;
using System.Collections.Generic;

namespace Taskdeck.Acceleration.Candidates.WorkModel;

public enum CandidateWorkRelationType
{
    RelatesTo,
    Blocks,
    DependsOn,
    Duplicates,
    SpawnedFrom
}

public sealed record CandidateWorkEndpoint(Guid Id, Guid OwnerUserId, Guid BoardId);

public sealed record CandidateWorkEdge(
    Guid FromId,
    Guid ToId,
    CandidateWorkRelationType Type);

public sealed record RelationValidationResult(
    bool IsValid,
    CandidateWorkEdge? CanonicalEdge = null,
    string? ErrorCode = null)
{
    public static RelationValidationResult Valid(CandidateWorkEdge edge) => new(true, edge);
    public static RelationValidationResult Invalid(string code) => new(false, null, code);
}

public static class WorkRelationRules
{
    public static RelationValidationResult ValidateAndCanonicalize(
        Guid fromId,
        Guid toId,
        CandidateWorkRelationType requestedType,
        IReadOnlyDictionary<Guid, CandidateWorkEndpoint> endpoints,
        IReadOnlyCollection<CandidateWorkEdge> existingEdges)
    {
        if (fromId == toId)
        {
            return RelationValidationResult.Invalid("work_link_self");
        }

        if (!endpoints.TryGetValue(fromId, out var from)
            || !endpoints.TryGetValue(toId, out var to))
        {
            return RelationValidationResult.Invalid("work_link_endpoint_not_found");
        }

        if (from.OwnerUserId != to.OwnerUserId || from.BoardId != to.BoardId)
        {
            return RelationValidationResult.Invalid("work_link_scope_mismatch");
        }

        var canonical = Canonicalize(fromId, toId, requestedType);
        foreach (var edge in existingEdges)
        {
            if (Canonicalize(edge.FromId, edge.ToId, edge.Type) == canonical)
            {
                return RelationValidationResult.Invalid("work_link_duplicate");
            }
        }

        if (canonical.Type == CandidateWorkRelationType.Blocks
            && HasPath(canonical.ToId, canonical.FromId, existingEdges))
        {
            return RelationValidationResult.Invalid("work_dependency_cycle");
        }

        return RelationValidationResult.Valid(canonical);
    }

    public static CandidateWorkEdge Canonicalize(
        Guid fromId,
        Guid toId,
        CandidateWorkRelationType requestedType)
    {
        return requestedType switch
        {
            CandidateWorkRelationType.RelatesTo => fromId.CompareTo(toId) <= 0
                ? new CandidateWorkEdge(fromId, toId, CandidateWorkRelationType.RelatesTo)
                : new CandidateWorkEdge(toId, fromId, CandidateWorkRelationType.RelatesTo),
            CandidateWorkRelationType.DependsOn =>
                new CandidateWorkEdge(toId, fromId, CandidateWorkRelationType.Blocks),
            _ => new CandidateWorkEdge(fromId, toId, requestedType)
        };
    }

    private static bool HasPath(
        Guid start,
        Guid target,
        IReadOnlyCollection<CandidateWorkEdge> edges)
    {
        var queue = new Queue<Guid>();
        var visited = new HashSet<Guid>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == target) return true;

            foreach (var edge in edges)
            {
                var canonical = Canonicalize(edge.FromId, edge.ToId, edge.Type);
                if (canonical.Type == CandidateWorkRelationType.Blocks && canonical.FromId == current)
                {
                    queue.Enqueue(canonical.ToId);
                }
            }
        }

        return false;
    }
}
