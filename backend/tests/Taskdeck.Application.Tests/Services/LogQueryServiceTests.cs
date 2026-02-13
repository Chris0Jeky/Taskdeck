using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LogQueryServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock = new();
    private readonly Mock<ICommandRunRepository> _commandRunRepoMock = new();
    private readonly LogQueryService _service;

    public LogQueryServiceTests()
    {
        _unitOfWorkMock.SetupGet(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.CommandRuns).Returns(_commandRunRepoMock.Object);

        _auditLogRepoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(Array.Empty<AuditLog>());
        _commandRunRepoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(Array.Empty<CommandRun>());

        _service = new LogQueryService(_unitOfWorkMock.Object);
    }

    [Fact]
    public async Task QueryLogsAsync_ShouldReturnValidationError_ForInvalidLimit()
    {
        var result = await _service.QueryLogsAsync(new LogQueryDto(Limit: 0), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task QueryLogsAsync_ShouldReturnValidationError_ForWideDateRange()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-40);
        var to = DateTimeOffset.UtcNow;

        var result = await _service.QueryLogsAsync(new LogQueryDto(From: from, To: to), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_ShouldReturnNotFound_WhenNoEntriesExist()
    {
        var result = await _service.GetByCorrelationIdAsync("missing-correlation", default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
