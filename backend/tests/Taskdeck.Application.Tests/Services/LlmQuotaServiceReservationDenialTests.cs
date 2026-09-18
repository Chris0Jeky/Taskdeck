using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class LlmQuotaServiceReservationDenialTests
{
    [Theory]
    [InlineData(
        QuotaReservationDecision.RequestsExceeded,
        12L,
        34_000L,
        98_000L,
        "Per-user hourly request limit (12) exceeded")]
    [InlineData(
        QuotaReservationDecision.TokensExceeded,
        12L,
        34_000L,
        98_000L,
        "Per-user daily token budget (34000) exhausted")]
    [InlineData(
        QuotaReservationDecision.GlobalExceeded,
        12L,
        34_000L,
        98_000L,
        "Global daily token budget exhausted")]
    public async Task ReserveAsync_MapsRepositoryDenialToStableServiceContract(
        QuotaReservationDecision decision,
        long requestsPerHour,
        long tokensPerDay,
        long globalBudgetCeilingTokens,
        string expectedReason)
    {
        var repository = new Mock<ILlmUsageRecordRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWork.SetupGet(candidate => candidate.LlmUsageRecords).Returns(repository.Object);

        var settings = new LlmQuotaSettings
        {
            RequestsPerHour = requestsPerHour,
            TokensPerDay = tokensPerDay,
            GlobalBudgetCeilingTokens = globalBudgetCeilingTokens,
            ReservationTtlSeconds = 90
        };
        var service = new LlmQuotaService(unitOfWork.Object, settings);
        var userId = Guid.NewGuid();
        const LlmSurface surface = LlmSurface.Chat;
        const int estimatedTokens = 512;
        using var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;

        repository
            .Setup(candidate => candidate.TryReserveAsync(
                userId,
                surface,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                requestsPerHour,
                tokensPerDay,
                globalBudgetCeilingTokens,
                estimatedTokens,
                It.IsAny<DateTimeOffset>(),
                cancellationToken))
            .ReturnsAsync(new QuotaReservationOutcome(
                decision,
                ReservationId: null,
                RequestCount: 41,
                UserTokens: 42_000,
                GlobalTokens: 99_000));

        var result = await service.ReserveAsync(
            userId,
            surface,
            estimatedTokens,
            cancellationToken);

        result.Allowed.Should().BeFalse();
        result.DeniedReason.Should().Be(expectedReason);
        result.ReservationId.Should().BeNull();
        result.RemainingRequests.Should().Be(0);
        result.RemainingTokens.Should().Be(0);

        repository.Verify(candidate => candidate.TryReserveAsync(
            userId,
            surface,
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            requestsPerHour,
            tokensPerDay,
            globalBudgetCeilingTokens,
            estimatedTokens,
            It.IsAny<DateTimeOffset>(),
            cancellationToken), Times.Once);
        repository.VerifyNoOtherCalls();
    }
}
