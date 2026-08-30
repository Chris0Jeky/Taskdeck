using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class AutomationProposalServiceArchivedBoardTests
{
    private const string ArchivedDecisionMessage =
        "Cannot modify proposals on an archived board. Restore the board before changing its decision history.";

    [Theory]
    [InlineData("applied")]
    [InlineData("failed")]
    public async Task DirectTerminalStatusSeams_CannotBypassArchivedBoardGuard(string status)
    {
        var userId = Guid.NewGuid();
        var board = new Board("Archived direct seam", ownerId: userId);
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            userId,
            "Archived direct status write",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            board.Id);
        proposal.Approve(userId);
        board.Archive();
        var proposals = new Mock<IAutomationProposalRepository>();
        var boards = new Mock<IBoardRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.AutomationProposals).Returns(proposals.Object);
        unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);
        proposals
            .Setup(repository => repository.GetByIdAsync(
                proposal.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        boards
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { board });
        var service = new AutomationProposalService(unitOfWork.Object);

        var result = status == "applied"
            ? await service.MarkAsAppliedAsync(proposal.Id)
            : await service.MarkAsFailedAsync(proposal.Id, "Operation failed");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Be(ArchivedDecisionMessage);
        proposal.Status.Should().Be(ProposalStatus.Approved);
        unitOfWork.Verify(
            work => work.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
