using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class RelationProposalReferenceTests
{
    public static IEnumerable<object[]> MalformedReferences()
    {
        foreach (var remove in new[] { false, true })
        foreach (var related in new[] { false, true })
        {
            var prefix = related ? "fedcba98" : "abcdef12";
            foreach (var reference in new[] { prefix[..1], prefix[..7], prefix + "-", "zzzzzzzz", prefix + "\n", " " + prefix, prefix + " " })
                yield return [remove, related, reference];
        }
    }

    [Theory]
    [MemberData(nameof(MalformedReferences))]
    public async Task MalformedReference_ShouldNotReachValidationOrProposalCreation(bool remove, bool related, string reference)
    {
        var fixture = new Fixture(remove);
        var arguments = fixture.Arguments(
            related ? fixture.First.Id.ToString() : reference,
            related ? reference : fixture.Second.Id.ToString());

        using var result = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.Context, arguments));

        result.RootElement.GetProperty("error").GetString().Should().Contain("unambiguous active cards");
        fixture.Relations.Verify(service => service.ValidateMutationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CardRelationEdge>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.AssertNoProposal();
        fixture.AssertNoBoardWrites();
    }

    [Theory]
    [InlineData(false, "short")]
    [InlineData(true, "short")]
    [InlineData(false, "SHORT")]
    [InlineData(true, "SHORT")]
    [InlineData(false, "D")]
    [InlineData(true, "D")]
    [InlineData(false, "N")]
    [InlineData(true, "N")]
    [InlineData(false, "B")]
    [InlineData(true, "B")]
    [InlineData(false, "P")]
    [InlineData(true, "P")]
    [InlineData(false, "X")]
    [InlineData(true, "X")]
    public async Task SupportedReferences_ShouldPreserveContextRevisionAndProposalOnlyBoundary(bool remove, string format)
    {
        var fixture = new Fixture(remove);
        string Reference(Guid id) => format switch
        {
            "short" => id.ToString("N")[..8],
            "SHORT" => id.ToString("N")[..8].ToUpperInvariant(),
            _ => id.ToString(format)
        };
        using var cancellation = new CancellationTokenSource();

        using var result = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.Context,
            fixture.Arguments(Reference(fixture.First.Id), Reference(fixture.Second.Id)), cancellation.Token));

        result.RootElement.GetProperty("full_proposal_id").GetGuid().Should().Be(fixture.ProposalId);
        fixture.Relations.Verify(service => service.ValidateMutationAsync(fixture.UserId, fixture.BoardId,
            new CardRelationEdge(fixture.First.Id, fixture.Second.Id, "blocks"), 17, remove, cancellation.Token), Times.Once);
        fixture.Cards.Verify(repository => repository.GetByBoardIdAsync(fixture.BoardId, cancellation.Token), Times.Once);
        fixture.Captured.Should().NotBeNull();
        var proposal = fixture.Captured!;
        proposal.RequestedByUserId.Should().Be(fixture.UserId);
        proposal.BoardId.Should().Be(fixture.BoardId);
        proposal.SourceType.Should().Be(ProposalSourceType.Chat);
        proposal.RiskLevel.Should().Be(RiskLevel.Medium);
        proposal.ProvenanceProvider.Should().Be("trusted-provider");
        proposal.ProvenanceModelId.Should().Be("trusted-model");
        proposal.ProvenancePromptVersion.Should().Be("trusted-prompt");
        var operation = proposal.Operations.Should().ContainSingle().Subject;
        operation.ActionType.Should().Be(remove ? "remove-relation" : "add-relation");
        operation.TargetId.Should().Be(fixture.First.Id.ToString());
        using var parameters = JsonDocument.Parse(operation.Parameters);
        parameters.RootElement.GetProperty("boardId").GetGuid().Should().Be(fixture.BoardId);
        parameters.RootElement.GetProperty("cardId").GetGuid().Should().Be(fixture.First.Id);
        parameters.RootElement.GetProperty("relatedCardId").GetGuid().Should().Be(fixture.Second.Id);
        parameters.RootElement.GetProperty("expectedRevision").GetInt64().Should().Be(17);
        fixture.Proposals.Verify(service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), cancellation.Token), Times.Once);
        fixture.AssertNoBoardWrites();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AmbiguousShortReference_ShouldRejectButExactGuidStillDisambiguates(bool remove, bool related)
    {
        var fixture = new Fixture(remove);
        var original = related ? fixture.Second : fixture.First;
        var collision = new FixedCard(fixture.BoardId,
            original.Id.ToString("D")[..8] + "-3333-4333-8333-333333333333");
        fixture.SetCards(fixture.First, fixture.Second, collision);
        using var rejected = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.Context,
            fixture.Arguments(related ? fixture.First.Id.ToString() : original.Id.ToString("N")[..8],
                related ? original.Id.ToString("N")[..8] : fixture.Second.Id.ToString())));

        rejected.RootElement.TryGetProperty("error", out _).Should().BeTrue();
        fixture.AssertNoProposal();
        using var accepted = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.Context,
            fixture.Arguments(fixture.First.Id.ToString(), fixture.Second.Id.ToString())));
        accepted.RootElement.GetProperty("full_proposal_id").GetGuid().Should().Be(fixture.ProposalId);
        fixture.AssertNoBoardWrites();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FullGuidOutsideLoadedBoard_ShouldNotBeAcceptedByParsingAlone(bool remove, bool related)
    {
        var fixture = new Fixture(remove);
        fixture.SetCards(related ? fixture.First : fixture.Second);

        using var result = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.Context,
            fixture.Arguments(fixture.First.Id.ToString(), fixture.Second.Id.ToString())));

        result.RootElement.TryGetProperty("error", out _).Should().BeTrue();
        fixture.AssertNoProposal();
        fixture.AssertNoBoardWrites();
    }

    [Theory]
    [InlineData(false, ErrorCodes.Forbidden)]
    [InlineData(true, ErrorCodes.Forbidden)]
    [InlineData(false, ErrorCodes.Conflict)]
    [InlineData(true, ErrorCodes.Conflict)]
    public async Task ValidReferences_DoNotBypassDownstreamAuthorityOrRevisionRefusal(bool remove, string code)
    {
        var fixture = new Fixture(remove);
        fixture.Relations.Setup(service => service.ValidateMutationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CardRelationEdge>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<BoardRelationsDto>(code, "Refused by relation contract"));

        using var result = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.Context,
            fixture.Arguments("abcdef12", "fedcba98")));

        result.RootElement.GetProperty("error").GetString().Should().Be("Refused by relation contract");
        fixture.AssertNoProposal();
        fixture.AssertNoBoardWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompatibilityHostWithoutRepository_AcceptsOnlyFullGuidsAndStillValidates(bool remove)
    {
        var fixture = new Fixture(remove, withRepository: false);
        using var rejected = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.Context,
            fixture.Arguments("abcdef12", "fedcba98")));
        rejected.RootElement.TryGetProperty("error", out _).Should().BeTrue();
        fixture.AssertNoProposal();

        using var accepted = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.Context,
            fixture.Arguments(fixture.First.Id.ToString("B"), fixture.Second.Id.ToString("N"))));
        accepted.RootElement.GetProperty("full_proposal_id").GetGuid().Should().Be(fixture.ProposalId);
        fixture.Relations.Verify(service => service.ValidateMutationAsync(fixture.UserId, fixture.BoardId,
            new CardRelationEdge(fixture.First.Id, fixture.Second.Id, "blocks"), 17, remove, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Cards.VerifyNoOtherCalls();
        fixture.AssertNoBoardWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoardOnlyOverload_StillRequiresTrustedUserContext(bool remove)
    {
        var fixture = new Fixture(remove);
        using var result = JsonDocument.Parse(await fixture.Executor.ExecuteAsync(fixture.BoardId,
            fixture.Arguments("abcdef12", "fedcba98")));
        result.RootElement.GetProperty("error").GetString().Should().Contain("requires user context");
        fixture.AssertNoProposal();
        fixture.Cards.VerifyNoOtherCalls();
        fixture.AssertNoBoardWrites();
    }

    private sealed class FixedCard : Card
    {
        public FixedCard(Guid boardId, string id) : base(boardId, Guid.NewGuid(), "Fixture card")
        {
            Id = Guid.Parse(id);
        }
    }

    private sealed class Fixture
    {
        public Guid BoardId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid ProposalId { get; } = Guid.NewGuid();
        public Card First { get; }
        public Card Second { get; }
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ICardRepository> Cards { get; } = new();
        public Mock<IBoardRelationService> Relations { get; } = new();
        public Mock<IAutomationProposalService> Proposals { get; } = new();
        public CreateProposalDto? Captured { get; private set; }
        public ProposeCardRelationExecutor Executor { get; }
        public ToolExecutionContext Context => new(BoardId, UserId,
            new ProposalProducerMetadata("trusted-provider", "trusted-model", "trusted-prompt"));

        public Fixture(bool remove, bool withRepository = true)
        {
            First = new FixedCard(BoardId, "abcdef12-1111-4111-8111-111111111111");
            Second = new FixedCard(BoardId, "fedcba98-2222-4222-8222-222222222222");
            UnitOfWork.Setup(unit => unit.Cards).Returns(Cards.Object);
            SetCards(First, Second);
            Relations.Setup(service => service.ValidateMutationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<CardRelationEdge>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(new BoardRelationsDto(BoardId, 17, [], true)));
            Proposals.Setup(service => service.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()))
                .Callback<CreateProposalDto, CancellationToken>((proposal, _) => Captured = proposal)
                .ReturnsAsync(Result.Success(new ProposalDto(
                    ProposalId, ProposalSourceType.Chat, null, BoardId, UserId,
                    ProposalStatus.PendingReview, RiskLevel.Medium, "Fixture proposal", null, null,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTime.UtcNow.AddDays(1),
                    null, null, null, null, Guid.NewGuid().ToString(), [])));
            Executor = remove
                ? new ProposeRemoveCardRelationExecutor(Proposals.Object, Relations.Object, withRepository ? UnitOfWork.Object : null)
                : new ProposeAddCardRelationExecutor(Proposals.Object, Relations.Object, withRepository ? UnitOfWork.Object : null);
        }

        public void SetCards(params Card[] cards) => Cards
            .Setup(repository => repository.GetByBoardIdAsync(BoardId, It.IsAny<CancellationToken>())).ReturnsAsync(cards);

        public JsonElement Arguments(string source, string target) => JsonSerializer.SerializeToElement(new
        {
            card_id = source, related_card_id = target, relation_type = "blocks", expected_revision = 17,
            // Identity/provenance attempts in untrusted tool arguments must not become the producer.
            boardId = Guid.NewGuid(), userId = Guid.NewGuid(), risk = "Low",
            provenanceProvider = "forged", provenanceModelId = "forged", provenancePromptVersion = "forged"
        });

        public void AssertNoProposal() => Proposals.Verify(service => service.CreateProposalAsync(
            It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()), Times.Never);

        public void AssertNoBoardWrites()
        {
            Relations.Verify(service => service.StageMutationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<CardRelationEdge>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
            Cards.Verify(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()), Times.Never);
            Cards.Verify(repository => repository.UpdateAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()), Times.Never);
            Cards.Verify(repository => repository.DeleteAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
