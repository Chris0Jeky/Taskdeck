using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AutomationProposalServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly AutomationProposalService _service;

    public AutomationProposalServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _notificationServiceMock = new Mock<INotificationService>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));

        _service = new AutomationProposalService(_unitOfWorkMock.Object, _notificationServiceMock.Object);
    }

    #region CreateProposalAsync Tests

    [Fact]
    public async Task CreateProposalAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var dto = new CreateProposalDto(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Create new card",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.AddAsync(It.IsAny<AutomationProposal>(), default))
            .ReturnsAsync((AutomationProposal p, CancellationToken ct) => p);

        // Act
        var result = await _service.CreateProposalAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Summary.Should().Be("Create new card");
        result.Value.Status.Should().Be(ProposalStatus.PendingReview);
        result.Value.RiskLevel.Should().Be(RiskLevel.Low);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateProposalAsync_ShouldAddOperations_WhenProvided()
    {
        // Arrange
        var operations = new List<CreateProposalOperationDto>
        {
            new(0, "card.create", "Card", "{\"name\":\"Test\"}", "key1"),
            new(1, "card.move", "Card", "{\"position\":5}", "key2", "card-123")
        };

        var dto = new CreateProposalDto(
            ProposalSourceType.Manual,
            Guid.NewGuid(),
            "Multi-step operation",
            RiskLevel.Medium,
            Guid.NewGuid().ToString(),
            Operations: operations);

        _proposalRepoMock.Setup(r => r.AddAsync(It.IsAny<AutomationProposal>(), default))
            .ReturnsAsync((AutomationProposal p, CancellationToken ct) => p);

        // Act
        var result = await _service.CreateProposalAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(2);
        result.Value.Operations[0].Sequence.Should().Be(0);
        result.Value.Operations[1].Sequence.Should().Be(1);
    }

    [Fact]
    public async Task CreateProposalAsync_ShouldReturnValidationError_WhenSummaryIsEmpty()
    {
        // Arrange
        var dto = new CreateProposalDto(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        // Act
        var result = await _service.CreateProposalAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region GetProposalByIdAsync Tests

    [Fact]
    public async Task GetProposalByIdAsync_ShouldReturnProposal_WhenExists()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalByIdAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(proposal.Id);
        result.Value.Summary.Should().Be("Test proposal");
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldBuildReadablePresentation_WhenOperationsExist()
    {
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Create the onboarding follow-up",
            RiskLevel.High,
            Guid.NewGuid().ToString(),
            boardId,
            sourceReferenceId: Guid.NewGuid().ToString());

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "card.create",
            "Card",
            "{\"title\":\"Draft follow-up\"}",
            Guid.NewGuid().ToString()));
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            1,
            "board.rename",
            "Board",
            "{\"name\":\"Support follow-up\"}",
            Guid.NewGuid().ToString(),
            boardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var result = await _service.GetProposalByIdAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Presentation.PlainSummary.Should().Contain("apply 2 planned changes");
        result.Value.Presentation.SourceCue.Should().Be("Created from Inbox capture triage.");
        result.Value.Presentation.RiskCue.Should().Contain("High risk");
        result.Value.Presentation.OperationHeadlines.Should().ContainInOrder(
            "Create card \"Draft follow-up\".",
            $"Rename board \"Support follow-up\".");
        result.Value.Presentation.AffectedEntities.Should().Contain(entity =>
            entity.EntityType == "Board" &&
            entity.EntityId == boardId.ToString() &&
            entity.ChangeCount == 1);
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync((AutomationProposal?)null);

        // Act
        var result = await _service.GetProposalByIdAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region ApproveProposalAsync Tests

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnSuccess_WhenPending()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, deciderId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Approved);
        result.Value.DecidedByUserId.Should().Be(deciderId);
        result.Value.DecidedAt.Should().NotBeNull();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
        _notificationServiceMock.Verify(
            s => s.PublishAsync(
                It.Is<CreateNotificationRequestDto>(n =>
                    n.UserId == proposal.RequestedByUserId &&
                    n.Type == NotificationType.ProposalOutcome),
                default),
            Times.Once);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnInvalidOperation_WhenAlreadyApproved()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.Approve(deciderId);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, Guid.NewGuid());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region RejectProposalAsync Tests

    [Fact]
    public async Task RejectProposalAsync_ShouldReturnSuccess_WhenPending()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.RejectProposalAsync(
            proposalId,
            deciderId,
            new UpdateProposalStatusDto("Not needed"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Rejected);
        result.Value.DecidedByUserId.Should().Be(deciderId);
        result.Value.FailureReason.Should().Be("Not needed");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RejectProposalAsync_ShouldRequireReason_ForHighRisk()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.High,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.RejectProposalAsync(
            proposalId,
            deciderId,
            new UpdateProposalStatusDto());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region MarkAsAppliedAsync Tests

    [Fact]
    public async Task MarkAsAppliedAsync_ShouldReturnSuccess_WhenApproved()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.MarkAsAppliedAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Applied);
        result.Value.AppliedAt.Should().NotBeNull();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task MarkAsAppliedAsync_ShouldReturnInvalidOperation_WhenNotApproved()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.MarkAsAppliedAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }

    #endregion

    #region MarkAsFailedAsync Tests

    [Fact]
    public async Task MarkAsFailedAsync_ShouldReturnSuccess_WhenApproved()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.MarkAsFailedAsync(proposalId, "Database error");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Failed);
        result.Value.FailureReason.Should().Be("Database error");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    #endregion

    #region ExpireProposalsAsync Tests

    [Fact]
    public async Task ExpireProposalsAsync_ShouldExpireAllStaleProposals()
    {
        // Arrange
        var proposal1 = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test 1",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1);

        var proposal2 = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test 2",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1);

        // Simulate that these are expired (repository would return expired ones)
        _proposalRepoMock.Setup(r => r.GetExpiredAsync(default))
            .ReturnsAsync(new[] { proposal1, proposal2 });

        // Act
        var result = await _service.ExpireProposalsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task ExpireProposalsAsync_ShouldReturnZero_WhenNoExpiredProposals()
    {
        // Arrange
        _proposalRepoMock.Setup(r => r.GetExpiredAsync(default))
            .ReturnsAsync(Array.Empty<AutomationProposal>());

        // Act
        var result = await _service.ExpireProposalsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region GetProposalDiffAsync Tests

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReturnDiff_WhenAvailable()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.SetDiffPreview("+ New card created\n- Old card removed");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("New card created");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReturnNotFound_WhenDiffNotAvailable()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region GetProposalsAsync Tests

    [Fact]
    public async Task GetProposalsAsync_ShouldFilterByStatus_WhenStatusProvided()
    {
        // Arrange
        var proposals = new[]
        {
            new AutomationProposal(ProposalSourceType.Chat, Guid.NewGuid(), "Test 1", RiskLevel.Low, Guid.NewGuid().ToString()),
            new AutomationProposal(ProposalSourceType.Chat, Guid.NewGuid(), "Test 2", RiskLevel.Low, Guid.NewGuid().ToString())
        };

        _proposalRepoMock.Setup(r => r.GetByStatusAsync(ProposalStatus.PendingReview, 100, default))
            .ReturnsAsync(proposals);

        // Act
        var result = await _service.GetProposalsAsync(new ProposalFilterDto(Status: ProposalStatus.PendingReview));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetProposalsAsync_ShouldFilterByBoardId_WhenProvided()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var proposals = new[]
        {
            new AutomationProposal(ProposalSourceType.Chat, Guid.NewGuid(), "Test", RiskLevel.Low, Guid.NewGuid().ToString(), boardId)
        };

        _proposalRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, 100, default))
            .ReturnsAsync(proposals);

        // Act
        var result = await _service.GetProposalsAsync(new ProposalFilterDto(BoardId: boardId));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetProposalsAsync_ShouldQueryByUserFirst_WhenUserAndStatusFiltersProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var pending = new AutomationProposal(ProposalSourceType.Chat, userId, "Pending", RiskLevel.Low, Guid.NewGuid().ToString());
        var approved = new AutomationProposal(ProposalSourceType.Chat, userId, "Approved", RiskLevel.Low, Guid.NewGuid().ToString());
        approved.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByUserIdAsync(userId, 10, default))
            .ReturnsAsync(new[] { pending, approved });

        // Act
        var result = await _service.GetProposalsAsync(new ProposalFilterDto(Status: ProposalStatus.PendingReview, UserId: userId, Limit: 10));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(p => p.Id == pending.Id);
        _proposalRepoMock.Verify(r => r.GetByUserIdAsync(userId, 10, default), Times.Once);
        _proposalRepoMock.Verify(r => r.GetByStatusAsync(It.IsAny<ProposalStatus>(), It.IsAny<int>(), default), Times.Never);
    }

    #endregion
}
