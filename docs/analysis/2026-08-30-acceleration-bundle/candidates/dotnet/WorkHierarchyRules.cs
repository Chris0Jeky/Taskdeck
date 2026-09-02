using System;
using System.Collections.Generic;
using System.Linq;

namespace Taskdeck.Acceleration.Candidates.WorkModel;

public sealed record CandidateWorkNode(
    Guid Id,
    Guid OwnerUserId,
    Guid BoardId,
    Guid? ParentId,
    bool IsArchived);

public sealed record HierarchyValidationResult(bool IsValid, string? ErrorCode = null)
{
    public static HierarchyValidationResult Valid() => new(true);
    public static HierarchyValidationResult Invalid(string code) => new(false, code);
}

public static class WorkHierarchyRules
{
    public static HierarchyValidationResult ValidateReparent(
        Guid childId,
        Guid? proposedParentId,
        IReadOnlyDictionary<Guid, CandidateWorkNode> nodes,
        int maximumDepth = 3)
    {
        if (maximumDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        if (!nodes.TryGetValue(childId, out var child))
        {
            return HierarchyValidationResult.Invalid("work_item_not_found");
        }

        if (proposedParentId is null)
        {
            return HierarchyValidationResult.Valid();
        }

        if (proposedParentId == childId)
        {
            return HierarchyValidationResult.Invalid("work_parent_self");
        }

        if (!nodes.TryGetValue(proposedParentId.Value, out var parent))
        {
            return HierarchyValidationResult.Invalid("work_parent_not_found");
        }

        if (child.OwnerUserId != parent.OwnerUserId || child.BoardId != parent.BoardId)
        {
            return HierarchyValidationResult.Invalid("work_parent_scope_mismatch");
        }

        if (parent.IsArchived)
        {
            return HierarchyValidationResult.Invalid("work_parent_archived");
        }

        var parentDepthResult = GetDepthAndDetectCycle(parent.Id, child.Id, nodes);
        if (!parentDepthResult.IsValid)
        {
            return parentDepthResult.Error!;
        }

        var subtreeHeightResult = GetSubtreeHeight(child.Id, nodes, new HashSet<Guid>());
        if (!subtreeHeightResult.IsValid)
        {
            return subtreeHeightResult.Error!;
        }

        var deepestResultingDepth = parentDepthResult.Value + subtreeHeightResult.Value;
        return deepestResultingDepth <= maximumDepth
            ? HierarchyValidationResult.Valid()
            : HierarchyValidationResult.Invalid("work_parent_depth_exceeded");
    }

    private static ValueResult GetDepthAndDetectCycle(
        Guid startId,
        Guid childBeingMoved,
        IReadOnlyDictionary<Guid, CandidateWorkNode> nodes)
    {
        var visited = new HashSet<Guid>();
        var currentId = startId;
        var depth = 0;

        while (true)
        {
            if (currentId == childBeingMoved)
            {
                return ValueResult.Invalid("work_parent_cycle");
            }

            if (!visited.Add(currentId))
            {
                return ValueResult.Invalid("work_hierarchy_corrupt_cycle");
            }

            if (!nodes.TryGetValue(currentId, out var current))
            {
                return ValueResult.Invalid("work_hierarchy_missing_ancestor");
            }

            depth++;
            if (current.ParentId is null)
            {
                return ValueResult.Valid(depth);
            }

            currentId = current.ParentId.Value;
        }
    }

    private static ValueResult GetSubtreeHeight(
        Guid rootId,
        IReadOnlyDictionary<Guid, CandidateWorkNode> nodes,
        HashSet<Guid> path)
    {
        if (!path.Add(rootId))
        {
            return ValueResult.Invalid("work_hierarchy_corrupt_cycle");
        }

        var maximumChildHeight = 0;
        foreach (var child in nodes.Values.Where(node => node.ParentId == rootId))
        {
            var childResult = GetSubtreeHeight(child.Id, nodes, path);
            if (!childResult.IsValid)
            {
                path.Remove(rootId);
                return childResult;
            }

            maximumChildHeight = Math.Max(maximumChildHeight, childResult.Value);
        }

        path.Remove(rootId);
        return ValueResult.Valid(1 + maximumChildHeight);
    }

    private sealed record ValueResult(bool IsValid, int Value, HierarchyValidationResult? Error)
    {
        public static ValueResult Valid(int value) => new(true, value, null);
        public static ValueResult Invalid(string code) => new(false, 0, HierarchyValidationResult.Invalid(code));
    }
}
