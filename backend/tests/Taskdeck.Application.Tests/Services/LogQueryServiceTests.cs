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

        _auditLogRepoMock
            .Setup(r => r.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditLog>());
        _commandRunRepoMock
            .Setup(r => r.QueryLogsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CommandRunLog>());

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

    [Fact]
    public async Task QueryLogsAsync_ShouldUseFilteredRepositoryQueries_InsteadOfFullTableComposition()
    {
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow;
        const int limit = 25;

        var result = await _service.QueryLogsAsync(new LogQueryDto(From: from, To: to, Limit: limit), default);

        result.IsSuccess.Should().BeTrue();

        _auditLogRepoMock.Verify(r => r.QueryAsync(
            from,
            to,
            null,
            null,
            null,
            null,
            limit,
            It.IsAny<CancellationToken>()), Times.Once);
        _commandRunRepoMock.Verify(r => r.QueryLogsAsync(
            from,
            to,
            null,
            null,
            null,
            null,
            limit,
            It.IsAny<CancellationToken>()), Times.Once);
        _auditLogRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        _commandRunRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        _commandRunRepoMock.Verify(r => r.GetByIdWithLogsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryLogsAsync_ShouldSkipAuditQuery_WhenCorrelationIdFilterProvided()
    {
        var result = await _service.QueryLogsAsync(new LogQueryDto(CorrelationId: "corr-123", Limit: 50), default);

        result.IsSuccess.Should().BeTrue();

        _auditLogRepoMock.Verify(r => r.QueryAsync(
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<Guid?>(),
            It.IsAny<Guid?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _commandRunRepoMock.Verify(r => r.QueryLogsAsync(
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<Guid?>(),
            "corr-123",
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            50,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
