using FluentAssertions;
using Moq;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CaptureResourcesErrorSafetyTests
{
    private const string HostileError =
        "Bearer tdsk_test_secret C:\\Users\\alice\\taskdeck.db " +
        "SQLite UNIQUE constraint failed: Users.Email https://provider.example/v1/internal";

    [Fact]
    public async Task ListCaptures_UnexpectedFailure_UsesGenericMessage()
    {
        var userId = Guid.NewGuid();
        var captureService = new Mock<ICaptureService>(MockBehavior.Strict);
        captureService
            .Setup(service => service.ListAsync(
                userId,
                It.IsAny<CaptureListFilterDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<CaptureItemSummaryDto>>(
                ErrorCodes.UnexpectedError,
                HostileError));

        var action = () => CreateResources(captureService.Object, userId).ListCaptures();

        var exception = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be(
            $"MCP: failed to list captures: {SensitiveDataRedactor.GenericUnexpectedFailureMessage}");
        exception.Message.Should().NotContain(HostileError);
        captureService.VerifyAll();
    }

    [Fact]
    public async Task GetCaptureDetail_UnexpectedFailure_UsesGenericMessage()
    {
        var userId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var captureService = new Mock<ICaptureService>(MockBehavior.Strict);
        captureService
            .Setup(service => service.GetByIdAsync(userId, captureId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CaptureItemDto>(ErrorCodes.UnexpectedError, HostileError));

        var action = () => CreateResources(captureService.Object, userId)
            .GetCaptureDetail(captureId.ToString());

        var exception = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be(
            $"MCP: failed to get capture: {SensitiveDataRedactor.GenericUnexpectedFailureMessage}");
        exception.Message.Should().NotContain(HostileError);
        captureService.VerifyAll();
    }

    [Fact]
    public async Task GetCaptureDetail_KnownDomainFailure_PreservesStableMessage()
    {
        const string stableMessage = "Capture item not found.";
        var userId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var captureService = new Mock<ICaptureService>(MockBehavior.Strict);
        captureService
            .Setup(service => service.GetByIdAsync(userId, captureId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CaptureItemDto>(ErrorCodes.NotFound, stableMessage));

        var action = () => CreateResources(captureService.Object, userId)
            .GetCaptureDetail(captureId.ToString());

        var exception = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be($"MCP: failed to get capture: {stableMessage}");
        captureService.VerifyAll();
    }

    private static CaptureResources CreateResources(
        ICaptureService captureService,
        Guid userId) =>
        new(captureService, new FixedUserContextProvider(userId));

    private sealed class FixedUserContextProvider(Guid userId) : IUserContextProvider
    {
        public Task<McpUserContext> GetCurrentContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new McpUserContext(userId, ApiKeyScope.Full));

        public Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(userId);

        public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(userId);
    }
}
