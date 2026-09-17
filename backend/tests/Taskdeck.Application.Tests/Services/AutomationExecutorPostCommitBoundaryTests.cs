using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AutomationExecutorPostCommitBoundaryTests
{
    [Fact]
    public async Task ExecuteProposalWithReceipt_ShouldKeepApplied_WhenCaptureLookupThrowsAfterCommit()
    {
        var fixture = new Fixture();
        var captureId = Guid.NewGuid();
        fixture.ArrangeApprovedQueueProposal(captureId);
        fixture.LlmQueue
            .Setup(repository => repository.GetByIdAsync(captureId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("capture adapter unavailable"));

        var result = await fixture.Service.ExecuteProposalWithReceiptAsync(
            fixture.ProposalId,
            "post-commit-capture-failure");

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyApplied.Should().BeFalse();
        result.Value.AppliedOperationCount.Should().Be(0);
        fixture.ProposalEntity.Status.Should().Be(ProposalStatus.Applied);
        fixture.UnitOfWork.Verify(
            unitOfWork => unitOfWork.CommitTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.UnitOfWork.Verify(
            unitOfWork => unitOfWork.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteProposalWithReceipt_ShouldIgnoreCallerCancellation_AfterCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        var captureId = Guid.NewGuid();
        fixture.ArrangeApprovedQueueProposal(captureId);
        fixture.UnitOfWork
            .Setup(unitOfWork => unitOfWork.CommitTransactionAsync(cancellation.Token))
            .Callback(cancellation.Cancel)
            .Returns(Task.CompletedTask);
        fixture.LlmQueue
            .Setup(repository => repository.GetByIdAsync(
                captureId,
                It.Is<CancellationToken>(token => token.IsCancellationRequested)))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        fixture.LlmQueue
            .Setup(repository => repository.GetByIdAsync(captureId, CancellationToken.None))
            .ReturnsAsync((LlmRequest?)null);

        var result = await fixture.Service.ExecuteProposalWithReceiptAsync(
            fixture.ProposalId,
            "post-commit-caller-cancelled",
            callerUserId: null,
            cancellation.Token);

        cancellation.IsCancellationRequested.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyApplied.Should().BeFalse();
        result.Value.AppliedOperationCount.Should().Be(0);
        fixture.ProposalEntity.Status.Should().Be(ProposalStatus.Applied);
        fixture.LlmQueue.Verify(
            repository => repository.GetByIdAsync(captureId, CancellationToken.None),
            Times.Once);
        fixture.UnitOfWork.Verify(
            unitOfWork => unitOfWork.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class Fixture
    {
        public Guid ProposalId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IAutomationProposalService> ProposalService { get; } = new();
        public Mock<IAutomationPolicyEngine> PolicyEngine { get; } = new();
        public Mock<IAutomationProposalRepository> Proposals { get; } = new();
        public Mock<ILlmQueueRepository> LlmQueue { get; } = new();
        public Mock<IProposalRevisionRepository> ProposalRevisions { get; } = new();
        public Mock<ILogger<AutomationExecutorService>> Logger { get; } = new();
        public AutomationProposal ProposalEntity { get; }
        public AutomationExecutorService Service { get; }

        public Fixture()
        {
            ProposalEntity = new AutomationProposal(
                ProposalSourceType.Manual,
                UserId,
                "Execution proposal",
                RiskLevel.Low,
                Guid.NewGuid().ToString());
            ProposalEntity.Approve(Guid.NewGuid());

            UnitOfWork.Setup(unitOfWork => unitOfWork.AutomationProposals).Returns(Proposals.Object);
            UnitOfWork.Setup(unitOfWork => unitOfWork.LlmQueue).Returns(LlmQueue.Object);
            UnitOfWork.Setup(unitOfWork => unitOfWork.ProposalRevisions).Returns(ProposalRevisions.Object);
            UnitOfWork
                .Setup(unitOfWork => unitOfWork.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            UnitOfWork
                .Setup(unitOfWork => unitOfWork.CommitTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            UnitOfWork
                .Setup(unitOfWork => unitOfWork.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            UnitOfWork
                .Setup(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            Proposals
                .Setup(repository => repository.GetByIdAsync(ProposalId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProposalEntity);
            PolicyEngine
                .Setup(engine => engine.GuardProposalDecisionWritesAsync(
                    It.IsAny<IEnumerable<Guid?>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());

            var cardService = new CardService(UnitOfWork.Object);
            var boardService = new BoardService(UnitOfWork.Object);
            var columnService = new ColumnService(UnitOfWork.Object);
            Service = new AutomationExecutorService(
                UnitOfWork.Object,
                ProposalService.Object,
                PolicyEngine.Object,
                cardService,
                boardService,
                columnService,
                Logger.Object);
        }

        public void ArrangeApprovedQueueProposal(Guid captureId)
        {
            var operations = Array.Empty<ProposalOperationDto>();
            var proposal = new ProposalDto(
                ProposalId,
                ProposalSourceType.Queue,
                captureId.ToString(),
                null,
                UserId,
                ProposalStatus.Approved,
                RiskLevel.Low,
                "Post-commit boundary",
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTime.UtcNow.AddDays(1),
                DateTime.UtcNow,
                Guid.NewGuid(),
                null,
                null,
                "post-commit-boundary",
                operations);

            ProposalService
                .Setup(service => service.GetProposalByIdAsync(
                    ProposalId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(proposal));
            PolicyEngine.Setup(engine => engine.ValidatePolicy(proposal)).Returns(Result.Success());
            PolicyEngine
                .Setup(engine => engine.ValidatePermissionsAsync(
                    UserId,
                    null,
                    It.IsAny<IEnumerable<ProposalOperationDto>>(),
                    BoardAccessBar.Write,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());
        }
    }
}
