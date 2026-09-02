using System;
using System.Collections.Generic;
using Taskdeck.Acceleration.Candidates.WorkModel;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.WorkModel;

public sealed class WorkHierarchyRulesTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Board = Guid.NewGuid();

    [Fact]
    public void Rejects_cycle()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var nodes = new Dictionary<Guid, CandidateWorkNode>
        {
            [root] = new(root, Owner, Board, null, false),
            [child] = new(child, Owner, Board, root, false)
        };

        var result = WorkHierarchyRules.ValidateReparent(root, child, nodes);
        Assert.False(result.IsValid);
        Assert.Equal("work_parent_cycle", result.ErrorCode);
    }

    [Fact]
    public void Rejects_move_when_descendant_would_exceed_depth()
    {
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var grandchild = Guid.NewGuid();
        var nodes = new Dictionary<Guid, CandidateWorkNode>
        {
            [parent] = new(parent, Owner, Board, null, false),
            [child] = new(child, Owner, Board, null, false),
            [grandchild] = new(grandchild, Owner, Board, child, false)
        };

        var result = WorkHierarchyRules.ValidateReparent(child, parent, nodes, maximumDepth: 2);
        Assert.False(result.IsValid);
        Assert.Equal("work_parent_depth_exceeded", result.ErrorCode);
    }
}
