using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class ProposalConflictOperationShapeTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Card _card;
    private readonly Column _column;
    private readonly Mock<IUnitOfWork> _unit = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly Mock<IRelatedProposalEvidenceService> _evidence = new();

    public ProposalConflictOperationShapeTests()
    {
        _column = new Column(_boardId, "Available", 0, wipLimit: 10);
        _card = new Card(_boardId, _column.Id, "Existing target");
        // The malformed identity-mismatch case references a card absent from this fixture.
        _unit.Setup(u => u.Cards.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Card?)null);
        _unit.Setup(u => u.Cards.GetByIdAsync(_card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_card);
        _unit.Setup(u => u.Columns.GetByIdWithCardsAsync(_column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_column);
        _unit.Setup(u => u.CardComments.CountByCardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _unit.Setup(u => u.OutboundWebhookSubscriptions.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutboundWebhookSubscription>());
        _authorization.Setup(a => a.CanReadBoardAsync(_userId, _boardId)).ReturnsAsync(Result.Success(true));
    }

    [Theory]
    [InlineData("replace-assignments", "card", "{}")]
    [InlineData("add-relation", "card", "{}")]
    [InlineData("remove-relation", "card", "{}")]
    [InlineData("add-label", "card", "{\"cardId\":\"CARD\"}")]
    [InlineData("remove-label", "card", "{\"cardId\":\"CARD\",\"labelId\":42}")]
    [InlineData("update", "card", "{\"title\":\"private-title\"}")]
    [InlineData("update", "card", "{\"cardId\":\"CARD\",\"workItemType\":17}")]
    [InlineData("update", "card", "{\"cardId\":\"CARD\",\"dueDate\":17}")]
    [InlineData("reorder", "column", "{\"columnId\":\"COLUMN\"}")]
    [InlineData("reorder", "column", "{\"columnId\":\"COLUMN\",\"position\":\"1\"}")]
    [InlineData("reorder", "column", "{\"columnId\":\"COLUMN\",\"position\":-1}")]
    [InlineData("create", "card", "{\"boardId\":\"BOARD\",\"columnId\":\"COLUMN\",\"title\":42}")]
    [InlineData("move", "card", "{\"cardId\":\"CARD\",\"columnId\":\"COLUMN\",\"clearParent\":false}")]
    [InlineData("update", "card", "{\"cardId\":\"OTHER\",\"title\":\"private-title\"}")]
    public async Task SupportedMalformedOperation_NeverProducesAffirmativeEvidence(string action, string target, string parameters)
    {
        var proposal = Proposal((action, target, parameters));

        var result = await Detector().DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(row => row.Key == "unable-to-evaluate-operation" && row.Tone == ConflictTone.Warn);
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        JsonSerializer.Serialize(result.Value).Should().NotContain("private-title");
    }

    [Theory]
    [InlineData("replace-assignments", "card", "{\"cardId\":\"CARD\",\"userIds\":[],\"expectedUpdatedAt\":\"STAMP\"}")]
    [InlineData("add-relation", "card", "{\"boardId\":\"BOARD\",\"cardId\":\"CARD\",\"relatedCardId\":\"OTHER\",\"relationType\":\"blocks\",\"expectedRevision\":0}")]
    [InlineData("remove-relation", "card", "{\"boardId\":\"BOARD\",\"cardId\":\"CARD\",\"relatedCardId\":\"OTHER\",\"relationType\":\"blocks\",\"expectedRevision\":0}")]
    [InlineData("add-label", "card", "{\"cardId\":\"CARD\",\"labelName\":\"Label\"}")]
    [InlineData("remove-label", "card", "{\"cardId\":\"CARD\",\"labelId\":\"OTHER\"}")]
    [InlineData("update", "card", "{\"cardId\":\"CARD\",\"title\":\"New title\"}")]
    [InlineData("reorder", "column", "{\"columnId\":\"COLUMN\",\"position\":1}")]
    [InlineData("create", "card", "{\"boardId\":\"BOARD\",\"columnId\":\"COLUMN\",\"title\":\"New card\"}")]
    public async Task SupportedCompleteOperation_IsNotClassifiedAsUnevaluable(string action, string target, string parameters)
    {
        var proposal = Proposal((action, target, parameters));

        var result = await Detector().DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(row => row.Key == "unable-to-evaluate-operation");
        result.Value.Should().Contain(row => row.Tone == ConflictTone.Ok);
    }

    [Fact]
    public async Task AssignmentAndAnotherWriteToSameCard_SuppressAffirmativeEvidence()
    {
        var proposal = Proposal(
            ("replace-assignments", "card", "{\"cardId\":\"CARD\",\"userIds\":[],\"expectedUpdatedAt\":\"STAMP\"}"),
            ("update", "card", "{\"cardId\":\"CARD\",\"title\":\"New title\"}"));

        var result = await Detector().DetectConflictsAsync(proposal.Id, _userId);

        result.Value.Should().ContainSingle(row => row.Key == "unable-to-evaluate-operation");
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
    }

    [Fact]
    public async Task TwoIndividuallyValidRelations_SuppressAffirmativeEvidence()
    {
        const string parameters = "{\"boardId\":\"BOARD\",\"cardId\":\"CARD\",\"relatedCardId\":\"OTHER\",\"relationType\":\"blocks\",\"expectedRevision\":0}";
        var proposal = Proposal(("add-relation", "card", parameters), ("remove-relation", "card", parameters));

        var result = await Detector().DetectConflictsAsync(proposal.Id, _userId);

        result.Value.Should().ContainSingle(row => row.Key == "unable-to-evaluate-operation");
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
    }

    private ProposalConflictDetector Detector() => new(_unit.Object, _authorization.Object, _evidence.Object);

    private AutomationProposal Proposal(params (string Action, string Target, string Parameters)[] operations)
    {
        var proposal = new AutomationProposal(ProposalSourceType.Manual, _userId, "Shape contract",
            RiskLevel.Low, Guid.NewGuid().ToString(), _boardId);
        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];
            var parameters = operation.Parameters.Replace("CARD", _card.Id.ToString(), StringComparison.Ordinal)
                .Replace("COLUMN", _column.Id.ToString(), StringComparison.Ordinal)
                .Replace("BOARD", _boardId.ToString(), StringComparison.Ordinal)
                .Replace("OTHER", "11111111-1111-1111-1111-111111111111", StringComparison.Ordinal)
                .Replace("STAMP", _card.UpdatedAt.ToString("O"), StringComparison.Ordinal);
            var targetId = operation.Target == "column" ? _column.Id : _card.Id;
            proposal.AddOperation(new AutomationProposalOperation(proposal.Id, index, operation.Action,
                operation.Target, parameters, Guid.NewGuid().ToString(), targetId.ToString()));
        }
        _unit.Setup(u => u.AutomationProposals.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(proposal);
        return proposal;
    }
}
