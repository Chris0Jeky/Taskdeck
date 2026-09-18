using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AutomationExecutorAssignmentPostCommitBoundaryTests
{
    [Fact]
    public async Task ExecuteProposalWithReceipt_ShouldRemainApplied_WhenAssignmentCardReloadThrowsAfterCommit()
    {
        var proposalId = Guid.NewGuid();
        var actor = new User("assignment-actor", "assignment-actor@example.com", "hash");
        var board = TestDataBuilder.CreateBoard("Assignment board");
        var column = TestDataBuilder.CreateColumn(board.Id, "To do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Assignment card");
        var operations = new List<ProposalOperationDto>
        {
            new(
                Guid.NewGuid(),
                proposalId,
                0,
                ProposalAssignmentContract.Action,
                "card",
                card.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    cardId = card.Id,
                    userIds = Array.Empty<Guid>(),
                    expectedUpdatedAt = card.UpdatedAt
                }),
                "post-commit-assignment",
                null)
        };
        var proposal = new ProposalDto(
            proposalId,
            ProposalSourceType.Manual,
            null,
            board.Id,
            actor.Id,
            ProposalStatus.Approved,
            RiskLevel.Low,
            "Post-commit assignment boundary",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow,
            Guid.NewGuid(),
            null,
            null,
            "post-commit-assignment-boundary",
            operations);
        var proposalEntity = new AutomationProposal(
            ProposalSourceType.Manual,
            actor.Id,
            "Post-commit assignment boundary",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        proposalEntity.Approve(Guid.NewGuid());

        var unitOfWork = new Mock<IUnitOfWork>();
        var proposalService = new Mock<IAutomationProposalService>();
        var policyEngine = new Mock<IAutomationPolicyEngine>();
        var proposals = new Mock<IAutomationProposalRepository>();
        var cards = new Mock<ICardRepository>();
        var boards = new Mock<IBoardRepository>();
        var users = new Mock<IUserRepository>();
        var auditLogs = new Mock<IAuditLogRepository>();
        var revisions = new Mock<IProposalRevisionRepository>();
        var assignmentStore = new Mock<ICardAssignmentStore>();
        var authorization = new Mock<IAuthorizationService>();
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var logger = new Mock<ILogger<AutomationExecutorService>>();

        unitOfWork.SetupGet(value => value.AutomationProposals).Returns(proposals.Object);
        unitOfWork.SetupGet(value => value.Cards).Returns(cards.Object);
        unitOfWork.SetupGet(value => value.Boards).Returns(boards.Object);
        unitOfWork.SetupGet(value => value.Users).Returns(users.Object);
        unitOfWork.SetupGet(value => value.AuditLogs).Returns(auditLogs.Object);
        unitOfWork.SetupGet(value => value.ProposalRevisions).Returns(revisions.Object);
        unitOfWork.Setup(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork.Setup(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork.Setup(value => value.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        proposalService
            .Setup(value => value.GetProposalByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(proposal));
        policyEngine.Setup(value => value.ValidatePolicy(It.IsAny<ProposalDto>()))
            .Returns(Result.Success());
        policyEngine
            .Setup(value => value.ValidateBoardAccessAsync(
                actor.Id,
                board.Id,
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        policyEngine
            .Setup(value => value.ValidatePermissionsAsync(
                actor.Id,
                board.Id,
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        policyEngine
            .Setup(value => value.GuardProposalDecisionWritesAsync(
                It.IsAny<IEnumerable<Guid?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        proposals
            .Setup(value => value.GetByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposalEntity);
        cards
            .SetupSequence(value => value.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card)
            .ThrowsAsync(new InvalidOperationException("post-commit card reload unavailable"));
        boards
            .Setup(value => value.GetByIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        users
            .Setup(value => value.GetByIdAsync(actor.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor);
        auditLogs
            .Setup(value => value.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLog auditLog, CancellationToken _) => auditLog);
        assignmentStore
            .Setup(value => value.RefreshAuthorityAsync(board.Id, actor.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        assignmentStore
            .Setup(value => value.ReadCardAsync(board.Id, card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        assignmentStore
            .Setup(value => value.ReadParticipantsAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<User>());
        authorization
            .Setup(value => value.CanWriteBoardAsync(actor.Id, board.Id))
            .ReturnsAsync(Result.Success(true));
        notifier
            .Setup(value => value.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var assignments = new CardAssignmentService(
            unitOfWork.Object,
            assignmentStore.Object,
            authorization.Object,
            notifier.Object);
        var service = new AutomationExecutorService(
            unitOfWork.Object,
            proposalService.Object,
            policyEngine.Object,
            new CardService(unitOfWork.Object),
            new BoardService(unitOfWork.Object),
            new ColumnService(unitOfWork.Object),
            logger.Object,
            assignments);

        var result = await service.ExecuteProposalWithReceiptAsync(
            proposalId,
            "post-commit-assignment-failure",
            actor.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyApplied.Should().BeFalse();
        result.Value.AppliedOperationCount.Should().Be(1);
        proposalEntity.Status.Should().Be(ProposalStatus.Applied);
        unitOfWork.Verify(
            value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(
            value => value.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        cards.Verify(
            value => value.GetByIdAsync(card.Id, CancellationToken.None),
            Times.Once);
    }
}
