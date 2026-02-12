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

public class AutomationPolicyEngineTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly AutomationPolicyEngine _engine;

    public AutomationPolicyEngineTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();
        _cardRepoMock = new Mock<ICardRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);

        _engine = new AutomationPolicyEngine(_unitOfWorkMock.Object);
    }

    #region ClassifyRisk Tests

    [Fact]
    public void ClassifyRisk_ShouldReturnLow_ForEmptyOperations()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>();

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnLow_ForSimpleCardCreate()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnMedium_ForArchiveOperation()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "archive", "card", "card1", "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnMedium_ForManyOperations()
    {
        // Arrange
        var operations = Enumerable.Range(0, 7)
            .Select(i => new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), i, "create", "card", null, "{}", $"key{i}", null))
            .ToList();

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnHigh_ForDeleteOperation()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "card", "card1", "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnHigh_ForBoardUpdate()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", "board1", "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnCritical_ForBoardDelete()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "board", "board1", "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnCritical_ForManyOperations()
    {
        // Arrange
        var operations = Enumerable.Range(0, 25)
            .Select(i => new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), i, "create", "card", null, "{}", $"key{i}", null))
            .ToList();

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Critical);
    }

    #endregion

    #region ValidatePermissions Tests

    [Fact]
    public async Task ValidatePermissions_ShouldReturnSuccess_ForValidUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var operations = new List<ProposalOperationDto>();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, null, operations);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnFailure_ForInvalidUserId()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>();

        // Act
        var result = await _engine.ValidatePermissionsAsync(Guid.Empty, null, operations);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnFailure_ForNonexistentUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, null, operations);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnFailure_ForNonexistentBoard()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnFailure_ForUnauthorizedBoardAccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.HasAccessAsync(boardId, userId, It.IsAny<Taskdeck.Domain.Enums.UserRole?>(), default))
            .ReturnsAsync(false);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    #endregion

    #region ValidatePolicy Tests

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForNullProposal()
    {
        // Act
        var result = _engine.ValidatePolicy(null!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForEmptyOperations()
    {
        // Arrange
        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
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

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForTooManyOperations()
    {
        // Arrange
        var operations = Enumerable.Range(0, 51)
            .Select(i => new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), i, "create", "card", null, "{}", $"key{i}", null))
            .ToList();

        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
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
            operations
        );

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("maximum operation count");
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForDuplicateSequences()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null),
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key2", null)
        };

        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
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
            operations
        );

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("sequences must be unique");
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForExpiredProposal()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
            ProposalStatus.PendingReview,
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

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("expired");
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnSuccess_ForValidProposal()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null),
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 1, "update", "card", "card1", "{}", "key2", null)
        };

        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
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
            operations
        );

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}
