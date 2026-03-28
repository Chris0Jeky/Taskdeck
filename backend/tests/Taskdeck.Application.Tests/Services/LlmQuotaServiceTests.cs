using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmQuotaServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ILlmUsageRecordRepository> _usageRepoMock = new();

    public LlmQuotaServiceTests()
    {
        _unitOfWorkMock.SetupGet(u => u.LlmUsageRecords).Returns(_usageRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _usageRepoMock
            .Setup(r => r.AddAsync(It.IsAny<LlmUsageRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmUsageRecord record, CancellationToken _) => record);
    }

    [Fact]
    public async Task RecordUsageAsync_ShouldPersistUsageRecord()
    {
        var settings = new LlmQuotaSettings { RequestsPerHour = 60, TokensPerDay = 100_000 };
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        await service.RecordUsageAsync(userId, LlmSurface.Chat, "OpenAI", "gpt-4", 100, 200);

        _usageRepoMock.Verify(
            r => r.AddAsync(It.Is<LlmUsageRecord>(rec =>
                rec.UserId == userId &&
                rec.Surface == LlmSurface.Chat &&
                rec.InputTokens == 100 &&
                rec.OutputTokens == 200),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CheckQuotaAsync_ShouldAllow_WhenWithinLimits()
    {
        var settings = new LlmQuotaSettings { RequestsPerHour = 60, TokensPerDay = 100_000 };
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        SetupRequestCount(userId, 10);
        SetupTotalTokens(userId, 5_000);
        SetupGlobalTokens(0);

        var result = await service.CheckQuotaAsync(userId, LlmSurface.Chat);

        result.Allowed.Should().BeTrue();
        result.DeniedReason.Should().BeNull();
        result.RemainingRequests.Should().Be(50);
        result.RemainingTokens.Should().Be(95_000);
    }

    [Fact]
    public async Task CheckQuotaAsync_ShouldDeny_WhenHourlyRequestLimitExceeded()
    {
        var settings = new LlmQuotaSettings { RequestsPerHour = 10, TokensPerDay = 100_000 };
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        SetupRequestCount(userId, 10);

        var result = await service.CheckQuotaAsync(userId, LlmSurface.Chat);

        result.Allowed.Should().BeFalse();
        result.DeniedReason.Should().Contain("hourly request limit");
        result.RemainingRequests.Should().Be(0);
    }

    [Fact]
    public async Task CheckQuotaAsync_ShouldDeny_WhenDailyTokenBudgetExhausted()
    {
        var settings = new LlmQuotaSettings { RequestsPerHour = 60, TokensPerDay = 50_000 };
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        SetupRequestCount(userId, 5);
        SetupTotalTokens(userId, 50_000);

        var result = await service.CheckQuotaAsync(userId, LlmSurface.Chat);

        result.Allowed.Should().BeFalse();
        result.DeniedReason.Should().Contain("daily token budget");
        result.RemainingTokens.Should().Be(0);
    }

    [Fact]
    public async Task CheckQuotaAsync_ShouldDeny_WhenGlobalBudgetCeilingExhausted()
    {
        var settings = new LlmQuotaSettings { RequestsPerHour = 60, TokensPerDay = 100_000, GlobalBudgetCeilingTokens = 200_000 };
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        SetupRequestCount(userId, 5);
        SetupTotalTokens(userId, 10_000);
        SetupGlobalTokens(200_000);

        var result = await service.CheckQuotaAsync(userId, LlmSurface.Chat);

        result.Allowed.Should().BeFalse();
        result.DeniedReason.Should().Contain("Global daily token budget");
    }

    [Fact]
    public async Task CheckQuotaAsync_ShouldAllow_WhenLimitsAreZero()
    {
        var settings = new LlmQuotaSettings { RequestsPerHour = 0, TokensPerDay = 0, GlobalBudgetCeilingTokens = 0 };
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        var result = await service.CheckQuotaAsync(userId, LlmSurface.Chat);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task GetUsageSummaryAsync_ShouldReturnCorrectAggregates()
    {
        var settings = new LlmQuotaSettings();
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        _usageRepoMock
            .Setup(r => r.GetUsageSummaryAsync(
                userId, null,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((500L, 1000L, 15L));

        var summary = await service.GetUsageSummaryAsync(userId);

        summary.TotalInputTokens.Should().Be(500);
        summary.TotalOutputTokens.Should().Be(1000);
        summary.TotalTokens.Should().Be(1500);
        summary.TotalRequests.Should().Be(15);
    }

    [Fact]
    public async Task GetQuotaStatusAsync_ShouldReturnCurrentState()
    {
        var settings = new LlmQuotaSettings { RequestsPerHour = 60, TokensPerDay = 100_000 };
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        SetupRequestCount(userId, 5);
        SetupTotalTokens(userId, 10_000);
        SetupGlobalTokens(0);

        var status = await service.GetQuotaStatusAsync(userId);

        status.Allowed.Should().BeTrue();
        status.TokensUsedToday.Should().Be(10_000);
        status.RequestsThisHour.Should().Be(5);
        status.TokenBudgetCeiling.Should().Be(100_000);
        status.RequestsPerHourLimit.Should().Be(60);
    }

    [Fact]
    public async Task CheckQuotaAsync_ShouldRecover_AfterTimeWindowResets()
    {
        // This tests that the time-based window is checked properly.
        // With 0 requests in the window, the user should be allowed.
        var settings = new LlmQuotaSettings { RequestsPerHour = 10, TokensPerDay = 100_000 };
        var service = new LlmQuotaService(_unitOfWorkMock.Object, settings);
        var userId = Guid.NewGuid();

        // Simulate time window reset — 0 requests in last hour
        SetupRequestCount(userId, 0);
        SetupTotalTokens(userId, 0);
        SetupGlobalTokens(0);

        var result = await service.CheckQuotaAsync(userId, LlmSurface.Chat);

        result.Allowed.Should().BeTrue();
        result.RemainingRequests.Should().Be(10);
    }

    private void SetupRequestCount(Guid userId, long count)
    {
        _usageRepoMock
            .Setup(r => r.GetRequestCountAsync(
                userId, null,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);
    }

    private void SetupTotalTokens(Guid userId, long tokens)
    {
        _usageRepoMock
            .Setup(r => r.GetTotalTokensAsync(
                userId, null,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
    }

    private void SetupGlobalTokens(long tokens)
    {
        _usageRepoMock
            .Setup(r => r.GetTotalTokensAsync(
                null, null,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
    }
}
