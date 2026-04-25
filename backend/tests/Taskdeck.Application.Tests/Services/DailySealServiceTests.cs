using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class DailySealServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IDailySnapshotRepository> _snapshotRepoMock;
    private readonly DailySealService _service;

    public DailySealServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _snapshotRepoMock = new Mock<IDailySnapshotRepository>();
        _unitOfWorkMock.Setup(u => u.DailySnapshots).Returns(_snapshotRepoMock.Object);
        _service = new DailySealService(_unitOfWorkMock.Object);
    }

    #region SealDayAsync

    [Fact]
    public async Task SealDayAsync_NewDay_CreatesAndSealsSnapshot()
    {
        var userId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        _snapshotRepoMock
            .Setup(r => r.GetByUserAndDateAsync(userId, date, default))
            .ReturnsAsync((DailySnapshot?)null);

        var result = await _service.SealDayAsync(userId, date);

        result.IsSuccess.Should().BeTrue();
        result.Value.WasAlreadySealed.Should().BeFalse();
        result.Value.SealedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        _snapshotRepoMock.Verify(r => r.AddAsync(It.IsAny<DailySnapshot>(), default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task SealDayAsync_UnsealedExistingDay_SealsSnapshot()
    {
        var userId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var existingSnapshot = new DailySnapshot(userId, date, DateTimeOffset.UtcNow);

        _snapshotRepoMock
            .Setup(r => r.GetByUserAndDateAsync(userId, date, default))
            .ReturnsAsync(existingSnapshot);

        var result = await _service.SealDayAsync(userId, date);

        result.IsSuccess.Should().BeTrue();
        result.Value.WasAlreadySealed.Should().BeFalse();
        result.Value.SealedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        _snapshotRepoMock.Verify(r => r.AddAsync(It.IsAny<DailySnapshot>(), default), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task SealDayAsync_AlreadySealedDay_IsIdempotent()
    {
        var userId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var existingSnapshot = new DailySnapshot(userId, date, DateTimeOffset.UtcNow);
        existingSnapshot.Seal(DateTimeOffset.UtcNow.AddHours(-1));
        var originalSealedAt = existingSnapshot.SealedAt!.Value;

        _snapshotRepoMock
            .Setup(r => r.GetByUserAndDateAsync(userId, date, default))
            .ReturnsAsync(existingSnapshot);

        var result = await _service.SealDayAsync(userId, date);

        result.IsSuccess.Should().BeTrue();
        result.Value.WasAlreadySealed.Should().BeTrue();
        result.Value.SealedAt.Should().Be(originalSealedAt);
    }

    [Fact]
    public async Task SealDayAsync_EmptyUserId_ReturnsValidationError()
    {
        var result = await _service.SealDayAsync(Guid.Empty, DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task SealDayAsync_FutureDate_ReturnsValidationError()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var result = await _service.SealDayAsync(Guid.NewGuid(), futureDate);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("future");
    }

    [Fact]
    public async Task SealDayAsync_PastDate_Succeeds()
    {
        var userId = Guid.NewGuid();
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

        _snapshotRepoMock
            .Setup(r => r.GetByUserAndDateAsync(userId, pastDate, default))
            .ReturnsAsync((DailySnapshot?)null);

        var result = await _service.SealDayAsync(userId, pastDate);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region GetSealStatusAsync

    [Fact]
    public async Task GetSealStatusAsync_NoSnapshot_ReturnsNotSealed()
    {
        var userId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        _snapshotRepoMock
            .Setup(r => r.GetByUserAndDateAsync(userId, date, default))
            .ReturnsAsync((DailySnapshot?)null);

        var result = await _service.GetSealStatusAsync(userId, date);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSealed.Should().BeFalse();
        result.Value.SealedAt.Should().BeNull();
        result.Value.Date.Should().Be(date);
    }

    [Fact]
    public async Task GetSealStatusAsync_SealedSnapshot_ReturnsSealedStatus()
    {
        var userId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var snapshot = new DailySnapshot(userId, date, DateTimeOffset.UtcNow);
        var sealTime = DateTimeOffset.UtcNow;
        snapshot.Seal(sealTime);

        _snapshotRepoMock
            .Setup(r => r.GetByUserAndDateAsync(userId, date, default))
            .ReturnsAsync(snapshot);

        var result = await _service.GetSealStatusAsync(userId, date);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSealed.Should().BeTrue();
        result.Value.SealedAt.Should().Be(sealTime);
    }

    [Fact]
    public async Task GetSealStatusAsync_UnsealedSnapshot_ReturnsNotSealed()
    {
        var userId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var snapshot = new DailySnapshot(userId, date, DateTimeOffset.UtcNow);

        _snapshotRepoMock
            .Setup(r => r.GetByUserAndDateAsync(userId, date, default))
            .ReturnsAsync(snapshot);

        var result = await _service.GetSealStatusAsync(userId, date);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSealed.Should().BeFalse();
        result.Value.SealedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetSealStatusAsync_EmptyUserId_ReturnsValidationError()
    {
        var result = await _service.GetSealStatusAsync(Guid.Empty, DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion
}
