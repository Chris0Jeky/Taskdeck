using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class HistoryServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly HistoryService _service;

    public HistoryServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();

        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);

        _service = new HistoryService(_unitOfWorkMock.Object);
    }

    #region GetBoardHistoryAsync Tests

    [Fact]
    public async Task GetBoardHistoryAsync_ShouldReturnAuditLogs()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var logs = new List<AuditLog>
        {
            new AuditLog("Board", boardId, AuditAction.Created, userId, "changes"),
            new AuditLog("Board", boardId, AuditAction.Updated, userId, "updated")
        };

        _auditLogRepoMock.Setup(r => r.GetByBoardAsync(boardId, 100, default))
            .ReturnsAsync(logs);

        // Act
        var result = await _service.GetBoardHistoryAsync(boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBoardHistoryAsync_ShouldReturnValidationError_WhenLimitIsOutOfRange()
    {
        var result = await _service.GetBoardHistoryAsync(Guid.NewGuid(), limit: 0);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _auditLogRepoMock.Verify(r => r.GetByBoardAsync(It.IsAny<Guid>(), It.IsAny<int>(), default), Times.Never);
    }

    #endregion

    #region GetEntityHistoryAsync Tests

    [Fact]
    public async Task GetEntityHistoryAsync_ShouldReturnAuditLogs()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var logs = new List<AuditLog>
        {
            new AuditLog("Card", entityId, AuditAction.Created, userId, "created card"),
            new AuditLog("Card", entityId, AuditAction.Moved, userId, "moved card")
        };

        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", entityId, 100, default))
            .ReturnsAsync(logs);

        // Act
        var result = await _service.GetEntityHistoryAsync("Card", entityId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetEntityHistoryAsync_ShouldReturnValidationError_WhenEntityTypeIsEmpty()
    {
        var result = await _service.GetEntityHistoryAsync(string.Empty, Guid.NewGuid(), 10);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _auditLogRepoMock.Verify(
            r => r.GetByEntityAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), default),
            Times.Never);
    }

    #endregion

    #region GetUserHistoryAsync Tests

    [Fact]
    public async Task GetUserHistoryAsync_ShouldReturnAuditLogs()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var logs = new List<AuditLog>
        {
            new AuditLog("Board", entityId, AuditAction.Created, userId, "created board")
        };

        _auditLogRepoMock.Setup(r => r.GetByUserAsync(userId, 100, default))
            .ReturnsAsync(logs);

        // Act
        var result = await _service.GetUserHistoryAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetUserHistoryAsync_ShouldReturnValidationError_WhenUserIdIsEmpty()
    {
        var result = await _service.GetUserHistoryAsync(Guid.Empty, 10);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _auditLogRepoMock.Verify(r => r.GetByUserAsync(It.IsAny<Guid>(), It.IsAny<int>(), default), Times.Never);
    }

    #endregion

    #region LogActionAsync Tests

    [Fact]
    public async Task LogActionAsync_ShouldReturnSuccess_WhenCreatingLogEntry()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.LogActionAsync("Board", entityId, AuditAction.Created, userId, "changes");

        // Assert
        result.IsSuccess.Should().BeTrue();
        _auditLogRepoMock.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    #endregion
}
