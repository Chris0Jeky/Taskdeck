using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AutomationExecutorServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAutomationProposalService> _proposalServiceMock;
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock;
    private readonly Mock<CardService> _cardServiceMock;
    private readonly Mock<BoardService> _boardServiceMock;
    private readonly Mock<ColumnService> _columnServiceMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly AutomationExecutorService _service;

    public AutomationExecutorServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalServiceMock = new Mock<IAutomationProposalService>();
        _policyEngineMock = new Mock<IAutomationPolicyEngine>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(default)).Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.CommitTransactionAsync(default)).Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.RollbackTransactionAsync(default)).Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog auditLog, CancellationToken _) => auditLog);

        // Use concrete service behavior through class mocks.
        _cardServiceMock = new Mock<CardService>(_unitOfWorkMock.Object);
        _boardServiceMock = new Mock<BoardService>(_unitOfWorkMock.Object);
        _columnServiceMock = new Mock<ColumnService>(_unitOfWorkMock.Object);

        _service = new AutomationExecutorService(
            _unitOfWorkMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _cardServiceMock.Object,
            _boardServiceMock.Object,
            _columnServiceMock.Object);
    }

    #region ExecuteProposal Tests

    [Fact]
    public async Task ExecuteProposal_ShouldReturnFailure_ForEmptyProposalId()
    {
        // Act
        var result = await _service.ExecuteProposalAsync(Guid.Empty, "key1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldReturnFailure_ForEmptyIdempotencyKey()
    {
        // Arrange
        var proposalId = Guid.NewGuid();

        // Act
        var result = await _service.ExecuteProposalAsync(proposalId, "");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("IdempotencyKey");
    }

    [Fact]
    public async Task ExecuteProposal_ShouldReturnFailure_ForNonexistentProposal()
    {
        // Arrange
        var proposalId = Guid.NewGuid();

        _proposalServiceMock.Setup(s => s.GetProposalByIdAsync(proposalId, default))
            .ReturnsAsync(Result.Failure<ProposalDto>(ErrorCodes.NotFound, "Not found"));

        // Act
        var result = await _service.ExecuteProposalAsync(proposalId, "key1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldReturnFailure_ForNonApprovedProposal()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var proposal = new ProposalDto(
            proposalId,
            ProposalSourceType.Manual,
            null,
            null,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "Test",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            null,
            null,
            null,
            null,
            "corr1",
            new List<ProposalOperationDto>()
        );

        _proposalServiceMock.Setup(s => s.GetProposalByIdAsync(proposalId, default))
            .ReturnsAsync(Result.Success(proposal));

        // Act
        var result = await _service.ExecuteProposalAsync(proposalId, "key1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("Cannot execute proposal");
    }

    [Fact]
    public async Task ExecuteProposal_ShouldReturnFailure_WhenPolicyValidationFails()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            new(Guid.NewGuid(), proposalId, 0, "create", "card", null, "{\"title\":\"Test\"}", "key1", null)
        };

        var proposal = new ProposalDto(
            proposalId,
            ProposalSourceType.Manual,
            null,
            null,
            userId,
            ProposalStatus.Approved,
            RiskLevel.Low,
            "Test",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(-1),
            null,
            null,
            null,
            null,
            "corr1",
            operations
        );

        _proposalServiceMock.Setup(s => s.GetProposalByIdAsync(proposalId, default))
            .ReturnsAsync(Result.Success(proposal));
        _policyEngineMock.Setup(e => e.ValidatePolicy(proposal))
            .Returns(Result.Failure(ErrorCodes.ValidationError, "Expired"));

        // Act
        var result = await _service.ExecuteProposalAsync(proposalId, "key1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldReturnFailure_WhenPermissionValidationFails()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            new(Guid.NewGuid(), proposalId, 0, "create", "card", null, "{\"title\":\"Test\"}", "key1", null)
        };

        var proposal = new ProposalDto(
            proposalId,
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.Approved,
            RiskLevel.Low,
            "Test",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            null,
            null,
            null,
            null,
            "corr1",
            operations
        );

        _proposalServiceMock.Setup(s => s.GetProposalByIdAsync(proposalId, default))
            .ReturnsAsync(Result.Success(proposal));
        _policyEngineMock.Setup(e => e.ValidatePolicy(proposal))
            .Returns(Result.Success());
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, operations, default))
            .ReturnsAsync(Result.Failure(ErrorCodes.Forbidden, "No access"));

        // Act
        var result = await _service.ExecuteProposalAsync(proposalId, "key1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldCommitAndMarkApplied_WhenOperationsSucceed()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        var operations = new List<ProposalOperationDto>
        {
            new(
                Guid.NewGuid(),
                proposalId,
                0,
                "update",
                "board",
                null,
                $$"""{"boardId":"{{board.Id}}","name":"Renamed Board"}""",
                "key1",
                null)
        };

        var proposal = CreateApprovedProposal(proposalId, userId, board.Id, operations);
        var proposalEntity = CreateApprovedProposalEntity(userId, board.Id);

        _proposalServiceMock.Setup(s => s.GetProposalByIdAsync(proposalId, default))
            .ReturnsAsync(Result.Success(proposal));
        _policyEngineMock.Setup(e => e.ValidatePolicy(proposal)).Returns(Result.Success());
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, board.Id, operations, default))
            .ReturnsAsync(Result.Success());
        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposalEntity);

        // Act
        var result = await _service.ExecuteProposalAsync(proposalId, "execution-key");

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(default), Times.Never);
        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.EntityType == "board" && a.EntityId == board.Id),
            default), Times.Once);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldRollbackAndMarkFailed_WhenLaterOperationFails()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        var operations = new List<ProposalOperationDto>
        {
            new(
                Guid.NewGuid(),
                proposalId,
                0,
                "update",
                "board",
                null,
                $$"""{"boardId":"{{board.Id}}","name":"Renamed Board"}""",
                "key1",
                null),
            new(
                Guid.NewGuid(),
                proposalId,
                1,
                "reorder",
                "column",
                null,
                """{"columnId":"invalid-guid","position":0}""",
                "key2",
                null)
        };

        var proposal = CreateApprovedProposal(proposalId, userId, board.Id, operations);
        var proposalEntity = CreateApprovedProposalEntity(userId, board.Id);

        _proposalServiceMock.Setup(s => s.GetProposalByIdAsync(proposalId, default))
            .ReturnsAsync(Result.Success(proposal));
        _policyEngineMock.Setup(e => e.ValidatePolicy(proposal)).Returns(Result.Success());
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, board.Id, operations, default))
            .ReturnsAsync(Result.Success());
        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposalEntity);

        // Act
        var result = await _service.ExecuteProposalAsync(proposalId, "execution-key");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Operation 1 (reorder column) failed");
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(default), Times.Never);
        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.EntityType == "board" && a.EntityId == board.Id),
            default), Times.Once);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldFailWithValidationError_ForMissingRequiredParameter()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            new(
                Guid.NewGuid(),
                proposalId,
                0,
                "create",
                "card",
                null,
                $$"""{"title":"Task","columnId":"{{Guid.NewGuid()}}"}""",
                "key1",
                null)
        };

        var proposal = CreateApprovedProposal(proposalId, userId, null, operations);
        var proposalEntity = CreateApprovedProposalEntity(userId);

        _proposalServiceMock.Setup(s => s.GetProposalByIdAsync(proposalId, default))
            .ReturnsAsync(Result.Success(proposal));
        _policyEngineMock.Setup(e => e.ValidatePolicy(proposal)).Returns(Result.Success());
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, null, operations, default))
            .ReturnsAsync(Result.Success());
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposalEntity);

        // Act
        var result = await _service.ExecuteProposalAsync(proposalId, "execution-key");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Missing required parameter 'boardId'");
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        _auditLogRepoMock.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), default), Times.Never);
    }

    #endregion

    private static ProposalDto CreateApprovedProposal(
        Guid proposalId,
        Guid userId,
        Guid? boardId,
        List<ProposalOperationDto> operations)
    {
        return new ProposalDto(
            proposalId,
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.Approved,
            RiskLevel.Low,
            "Test Proposal",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow,
            Guid.NewGuid(),
            null,
            null,
            "corr1",
            operations);
    }

    private static AutomationProposal CreateApprovedProposalEntity(Guid userId, Guid? boardId = null)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Manual,
            userId,
            "Execution proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        proposal.Approve(Guid.NewGuid());
        return proposal;
    }
}
