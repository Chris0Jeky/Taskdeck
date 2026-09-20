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

public sealed class ProposalConflictEntityScopeTests
{
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _board = Guid.NewGuid();
    private readonly Guid _foreignBoard = Guid.NewGuid();
    private readonly Mock<IUnitOfWork> _unit = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly Mock<IRelatedProposalEvidenceService> _related = new();
    private readonly Card _ownCard;
    private readonly Card _foreignCard;
    private readonly Column _foreignColumn;

    public ProposalConflictEntityScopeTests()
    {
        _foreignColumn = new Column(_foreignBoard, "foreign-column-secret", 0, wipLimit: 10);
        _foreignCard = new Card(_foreignBoard, _foreignColumn.Id, "foreign-card-secret");
        _ownCard = new Card(_board, Guid.NewGuid(), "visible-card");
        _unit.Setup(u => u.Cards.GetByIdAsync(_foreignCard.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_foreignCard);
        _unit.Setup(u => u.Cards.GetByIdAsync(_ownCard.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ownCard);
        _unit.Setup(u => u.Columns.GetByIdWithCardsAsync(_foreignColumn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_foreignColumn);
        _unit.Setup(u => u.CardComments.CountByCardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(7);
        _unit.Setup(u => u.OutboundWebhookSubscriptions.GetActiveByBoardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutboundWebhookSubscription>());
        _authorization.Setup(a => a.CanReadBoardAsync(_user, _board)).ReturnsAsync(Result.Success(true));
        _authorization.Setup(a => a.CanReadBoardAsync(_user, _foreignBoard)).ReturnsAsync(Result.Success(false));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HiddenCard_HasOnlyMissingTargetEvidence_AndDoesNotReadComments(bool boardless, bool parameterOnly)
    {
        var proposal = Proposal(boardless, "update", new { cardId = _foreignCard.Id, title = "Proposed title" },
            parameterOnly ? null : _foreignCard.Id);

        var result = await Detector().DetectConflictsAsync(proposal.Id, _user);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(row => row.Key == "missing-target" && row.Tone == ConflictTone.Warn);
        result.Value.Should().NotContain(row => row.Key == "active-comments" || row.Key == "fresh-data");
        JsonSerializer.Serialize(result.Value).Should().NotContain("foreign-card-secret");
        _unit.Verify(u => u.CardComments.CountByCardIdAsync(_foreignCard.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenColumn_DoesNotExposeItsNameOrCapacity(bool boardless)
    {
        var proposal = Proposal(boardless, "move", new { cardId = _ownCard.Id, columnId = _foreignColumn.Id }, _ownCard.Id);

        var result = await Detector().DetectConflictsAsync(proposal.Id, _user);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(row => row.Key == "missing-target-column" && row.Tone == ConflictTone.Warn);
        result.Value.Should().NotContain(row => row.Key == "capacity" || row.Key == "wip-limit");
        JsonSerializer.Serialize(result.Value).Should().NotContain("foreign-column-secret");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AccessibleCard_RetainsFreshDataAndComments(bool boardless, bool parameterOnly)
    {
        var proposal = Proposal(boardless, "update", new { cardId = _ownCard.Id, title = "Proposed title" },
            parameterOnly ? null : _ownCard.Id);

        var result = await Detector().DetectConflictsAsync(proposal.Id, _user);

        result.Value.Should().Contain(row => row.Key == "fresh-data" && row.Value.Contains("visible-card", StringComparison.Ordinal));
        result.Value.Should().Contain(row => row.Key == "active-comments");
        result.Value.Should().NotContain(row => row.Key == "missing-target");
        _unit.Verify(u => u.Cards.GetByIdAsync(_ownCard.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BoardlessPermission_IsRecheckedOnEveryRequest_NotCachedOnTheDetector()
    {
        _authorization.SetupSequence(a => a.CanReadBoardAsync(_user, _foreignBoard))
            .ReturnsAsync(Result.Success(true)).ReturnsAsync(Result.Success(false));
        var proposal = Proposal(true, "update", new { cardId = _foreignCard.Id, title = "Proposed title" }, _foreignCard.Id);
        var detector = Detector();

        var before = await detector.DetectConflictsAsync(proposal.Id, _user);
        var after = await detector.DetectConflictsAsync(proposal.Id, _user);

        before.Value.Should().Contain(row => row.Key == "fresh-data");
        after.Value.Should().Contain(row => row.Key == "missing-target");
        JsonSerializer.Serialize(after.Value).Should().NotContain("foreign-card-secret");
        _authorization.Verify(a => a.CanReadBoardAsync(_user, _foreignBoard), Times.Exactly(2));
        _unit.Verify(u => u.CardComments.CountByCardIdAsync(_foreignCard.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForeignBoardReadPermission_DoesNotOverrideTheProposalBoardScope()
    {
        _authorization.Setup(a => a.CanReadBoardAsync(_user, _foreignBoard)).ReturnsAsync(Result.Success(true));
        var proposal = Proposal(false, "update", new { cardId = _foreignCard.Id, title = "Proposed title" }, _foreignCard.Id);

        var result = await Detector().DetectConflictsAsync(proposal.Id, _user);

        result.Value.Should().Contain(row => row.Key == "missing-target");
        JsonSerializer.Serialize(result.Value).Should().NotContain("foreign-card-secret");
        _authorization.Verify(a => a.CanReadBoardAsync(_user, _foreignBoard), Times.Never);
    }

    private ProposalConflictDetector Detector() => new(_unit.Object, _authorization.Object, _related.Object);

    private AutomationProposal Proposal(bool boardless, string action, object parameters, Guid? targetId)
    {
        var proposal = new AutomationProposal(ProposalSourceType.Manual, _user, "Entity scope",
            RiskLevel.High, Guid.NewGuid().ToString(), boardless ? null : _board);
        proposal.AddOperation(new AutomationProposalOperation(proposal.Id, 0, action, "card",
            JsonSerializer.Serialize(parameters), Guid.NewGuid().ToString(), targetId?.ToString()));
        _unit.Setup(u => u.AutomationProposals.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(proposal);
        return proposal;
    }
}
