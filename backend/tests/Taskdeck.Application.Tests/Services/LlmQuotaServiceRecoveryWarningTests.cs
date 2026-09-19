using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class LlmQuotaServiceRecoveryWarningTests
{
    [Fact]
    public async Task CommitReservationAsync_WhenExpiredReservationIsRecovered_LogsActionableIdentityWithoutRepersisting()
    {
        var repository = new Mock<ILlmUsageRecordRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWork.SetupGet(candidate => candidate.LlmUsageRecords).Returns(repository.Object);

        var logger = new InMemoryLogger<LlmQuotaService>();
        var settings = new LlmQuotaSettings { ReservationTtlSeconds = 137 };
        var service = new LlmQuotaService(unitOfWork.Object, settings, logger);
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const LlmSurface surface = LlmSurface.Chat;
        const string provider = "OpenAI";
        const string model = "gpt-4o-mini";
        const int inputTokens = 123;
        const int outputTokens = 45;
        using var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;

        repository
            .Setup(candidate => candidate.CommitReservationAsync(
                reservationId,
                userId,
                surface,
                provider,
                model,
                inputTokens,
                outputTokens,
                cancellationToken))
            .ReturnsAsync(QuotaCommitResult.RecoveredExpired);

        await service.CommitReservationAsync(
            reservationId,
            userId,
            surface,
            provider,
            model,
            inputTokens,
            outputTokens,
            cancellationToken);

        var warning = logger.Entries.Should()
            .ContainSingle(entry => entry.Level == LogLevel.Warning)
            .Which;
        warning.Message.Should().Contain(reservationId.ToString());
        warning.Message.Should().Contain("after 137s");
        warning.Message.Should().Contain("168 billed tokens recovered");

        repository.Verify(candidate => candidate.CommitReservationAsync(
            reservationId,
            userId,
            surface,
            provider,
            model,
            inputTokens,
            outputTokens,
            cancellationToken), Times.Once);
        repository.VerifyNoOtherCalls();
        unitOfWork.VerifyGet(candidate => candidate.LlmUsageRecords, Times.Once);
        unitOfWork.Verify(
            candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
