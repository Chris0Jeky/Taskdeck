using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LogQueryServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<ICommandRunRepository> _commandRunRepoMock;
    private readonly LogQueryService _service;

    public LogQueryServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _commandRunRepoMock = new Mock<ICommandRunRepository>();

        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.CommandRuns).Returns(_commandRunRepoMock.Object);

        _service = new LogQueryService(_unitOfWorkMock.Object);
    }

    #region QueryLogsAsync Tests

    [Fact]
    public async Task QueryLogsAsync_ShouldReturnLogs_WithNoFilters()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var auditLogs = new List<AuditLog>
        {
            new AuditLog("Board", entityId, AuditAction.Created, userId),
            new AuditLog("Card", entityId, AuditAction.Updated, userId)
        };
        var query = new LogQueryDto();

        _auditLogRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(auditLogs);

        // Act
        var result = await _service.QueryLogsAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryLogsAsync_ShouldFilterByLevel()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var auditLogs = new List<AuditLog>
        {
            new AuditLog("Board", entityId, AuditAction.Created, userId),   // Info
            new AuditLog("Card", entityId, AuditAction.Deleted, userId)     // Warning
        };
        var query = new LogQueryDto(Level: "Warning");

        _auditLogRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(auditLogs);

        // Act
        var result = await _service.QueryLogsAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Level.Should().Be("Warning");
    }

    [Fact]
    public async Task QueryLogsAsync_ShouldFilterBySource()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var auditLogs = new List<AuditLog>
        {
            new AuditLog("Board", entityId, AuditAction.Created, userId),
            new AuditLog("Card", entityId, AuditAction.Updated, userId)
        };
        var query = new LogQueryDto(Source: "Board");

        _auditLogRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(auditLogs);

        // Act
        var result = await _service.QueryLogsAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Source.Should().Be("Board");
    }

    [Fact]
    public async Task QueryLogsAsync_ShouldFilterByUserId()
    {
        // Arrange
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var auditLogs = new List<AuditLog>
        {
            new AuditLog("Board", entityId, AuditAction.Created, userId1),
            new AuditLog("Card", entityId, AuditAction.Updated, userId2)
        };
        var query = new LogQueryDto(UserId: userId1);

        _auditLogRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(auditLogs);

        // Act
        var result = await _service.QueryLogsAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().UserId.Should().Be(userId1);
    }

    #endregion

    #region GetByCorrelationIdAsync Tests

    [Fact]
    public async Task GetByCorrelationIdAsync_ShouldReturnValidationError_WhenEmpty()
    {
        // Arrange & Act
        var result = await _service.GetByCorrelationIdAsync("");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Correlation ID cannot be empty");
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_ShouldReturnEntries_WhenCommandRunFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString("N");
        var commandRun = new CommandRun("health.check", userId, correlationId);
        commandRun.Start();
        commandRun.AddLog(new CommandRunLog(commandRun.Id, "Info", "OpsCliService", "Command completed"));
        commandRun.Complete(0);

        _commandRunRepoMock.Setup(r => r.GetByCorrelationIdAsync(correlationId, default))
            .ReturnsAsync(commandRun);
        _commandRunRepoMock.Setup(r => r.GetByIdWithLogsAsync(commandRun.Id, default))
            .ReturnsAsync(commandRun);

        // Act
        var result = await _service.GetByCorrelationIdAsync(correlationId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Message.Should().Be("Command completed");
        result.Value.First().CorrelationId.Should().Be(correlationId);
    }

    #endregion
}
