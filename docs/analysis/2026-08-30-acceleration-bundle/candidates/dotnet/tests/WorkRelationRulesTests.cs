using System;
using System.Collections.Generic;
using Taskdeck.Acceleration.Candidates.WorkModel;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.WorkModel;

public sealed class WorkRelationRulesTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Board = Guid.NewGuid();

    [Fact]
    public void Relates_to_is_canonical_and_reverse_duplicate_is_rejected()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var endpoints = new Dictionary<Guid, CandidateWorkEndpoint>
        {
            [a] = new(a, Owner, Board),
            [b] = new(b, Owner, Board)
        };
        var existing = new[] { new CandidateWorkEdge(a, b, CandidateWorkRelationType.RelatesTo) };
        var result = WorkRelationRules.ValidateAndCanonicalize(b, a, CandidateWorkRelationType.RelatesTo, endpoints, existing);
        Assert.False(result.IsValid);
        Assert.Equal("work_link_duplicate", result.ErrorCode);
    }

    [Fact]
    public void Depends_on_is_normalised_to_blocks_and_cycle_is_rejected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var endpoints = new Dictionary<Guid, CandidateWorkEndpoint>
        {
            [a] = new(a, Owner, Board),
            [b] = new(b, Owner, Board)
        };
        var existing = new[] { new CandidateWorkEdge(a, b, CandidateWorkRelationType.Blocks) };
        var result = WorkRelationRules.ValidateAndCanonicalize(a, b, CandidateWorkRelationType.DependsOn, endpoints, existing);
        Assert.False(result.IsValid);
        Assert.Equal("work_dependency_cycle", result.ErrorCode);
    }
}
