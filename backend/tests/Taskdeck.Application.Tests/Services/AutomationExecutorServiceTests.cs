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
    private readonly AutomationExecutorService _service;

    public AutomationExecutorServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalServiceMock = new Mock<IAutomationProposalService>();
        _policyEngineMock = new Mock<IAutomationPolicyEngine>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);

        // Create mocks for services - they need IUnitOfWork in constructor
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
            ProposalStatus.PendingReview, // Not approved
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
            new ProposalOperationDto(Guid.NewGuid(), proposalId, 0, "create", "card", null, "{\"title\":\"Test\"}", "key1", null)
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
            DateTime.UtcNow.AddDays(-1), // Expired
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
            new ProposalOperationDto(Guid.NewGuid(), proposalId, 0, "create", "card", null, "{\"title\":\"Test\"}", "key1", null)
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

    #endregion
}
