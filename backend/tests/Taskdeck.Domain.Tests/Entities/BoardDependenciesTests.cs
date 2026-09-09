using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;
namespace Taskdeck.Domain.Tests.Entities;

public sealed class BoardDependenciesTests
{
    [Fact]
    public void AcceptsDiamondAndRejectsCycleWithoutChangingSavedGraph()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid();
        var graph = new BoardDependencies(Guid.NewGuid());
        CardDependency[] edges = [new(a, b), new(a, c), new(b, d), new(c, d)];
        graph.Replace(edges);
        var act = () => graph.Replace([.. edges, new(d, a)]);
        act.Should().Throw<DomainException>();
        graph.Revision.Should().Be(1);
        graph.ReadEdges().Should().Equal(edges);
    }
    [Fact]
    public void BoundsInputAndRejectsInvalidReferencesAndDuplicates()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var graph = new BoardDependencies(Guid.NewGuid());
        CardDependency[][] bad = [[new(a, a)], [new(a, Guid.Empty)], [new(a, b), new(a, b)], [null!]];
        foreach (var edges in bad) { var act = () => graph.Replace(edges); act.Should().Throw<DomainException>(); }
        var nodes = Enumerable.Range(0, 502).Select(_ => Guid.NewGuid()).ToArray();
        var chain = nodes.Skip(1).Select((id, i) => new CardDependency(nodes[i], id)).ToArray();
        var tooMany = () => graph.Replace(chain);
        tooMany.Should().Throw<DomainException>();
        graph.Replace(chain.Take(500).ToArray());
        graph.ReadEdges().Should().HaveCount(500);
        graph.Replace([]);
        graph.ReadEdges().Should().BeEmpty();
    }
}
