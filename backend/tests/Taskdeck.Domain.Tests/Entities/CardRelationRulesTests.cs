using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;
namespace Taskdeck.Domain.Tests.Entities;

public class CardRelationRulesTests
{
    private readonly Guid board = Guid.NewGuid();
    private readonly Guid a = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private readonly Guid b = Guid.Parse("00000002-0000-0000-0000-000000000000");
    private readonly Guid c = Guid.Parse("00000003-0000-0000-0000-000000000000");
    [Fact]
    public void NormalizesInverseAndSymmetricAliases_AndRetainsDirectedMeaning()
    {
        CardRelationRules.Normalize(new(a,b,"depends-on")).Should().Be(new CardRelationEdge(b,a,"blocks"));
        CardRelationRules.Normalize(new(b,a,"relates-to")).Should().Be(new CardRelationEdge(a,b,"relates-to"));
        foreach (var kind in new[] { "duplicates", "spawned-from" })
            CardRelationRules.Normalize(new(b,a,kind)).Should().Be(new CardRelationEdge(b,a,kind));
        var duplicate = () => CardRelationRules.Validate([new(a,b,"depends-on"), new(b,a,"blocks")]);
        duplicate.Should().Throw<DomainException>();
    }
    [Theory]
    [InlineData("blocks")]
    [InlineData("duplicates")]
    [InlineData("spawned-from")]
    public void EachDirectedKindRejectsCycles_ButIndependentKindsCanCross(string kind)
    {
        var cycle = () => CardRelationRules.Validate([new(a,b,kind),new(b,c,kind),new(c,a,kind)]);
        cycle.Should().Throw<DomainException>();
        CardRelationRules.Validate([new(a,b,"duplicates"),new(b,a,"blocks")]).Should().HaveCount(2);
        CardRelationRules.Validate([new(a,b,"relates-to"),new(b,c,"relates-to"),new(c,a,"relates-to")]).Should().HaveCount(3);
    }
    [Fact]
    public void RejectsInvalidEndpointTypeDuplicateAndOverallBound()
    {
        foreach (var edge in new[] { new CardRelationEdge(a,a,"blocks"),new(a,Guid.Empty,"blocks"),new(a,b,"Blocks"),null! })
        {
            var invalid = () => CardRelationRules.Normalize(edge);
            invalid.Should().Throw<DomainException>();
        }
        var tooMany = () => CardRelationRules.Validate(Enumerable.Range(0,501).Select(_ => new CardRelationEdge(a,Guid.NewGuid(),"relates-to")).ToArray());
        tooMany.Should().Throw<DomainException>();
        CardRelationEndpoint[] endpoints = [new(a,board,false),new(b,board,true),new(c,Guid.NewGuid(),false)];
        var archived = () => CardRelationRules.Apply(board,[],new(a,b,"blocks"),false,endpoints);
        var foreign = () => CardRelationRules.Apply(board,[],new(a,c,"blocks"),false,endpoints);
        archived.Should().Throw<DomainException>();
        foreign.Should().Throw<DomainException>();
        CardRelationRules.Validate(board,[new(a,b,"blocks")],endpoints).Should().HaveCount(1);
    }
    [Fact]
    public void LegacyReplacementPreservesTypedRelations_AndFailedReplacementIsAtomic()
    {
        var graph = new BoardDependencies(board);
        graph.ReplaceRelations([new(a,b,"duplicates")]);
        graph.Replace([new(a,b)]);
        graph.ReadRelations().Should().BeEquivalentTo(new[] { new CardRelationEdge(a,b,"duplicates"),new(b,a,"blocks") });
        var bad = () => graph.Replace([new(a,b),new(b,a)]);
        bad.Should().Throw<DomainException>();
        graph.Revision.Should().Be(2);
        graph.Replace([]);
        graph.ReadRelations().Should().ContainSingle().Which.RelationType.Should().Be("duplicates");
    }
}
