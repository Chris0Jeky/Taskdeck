using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class LlmRequestTests
{
    [Fact]
    public void Constructor_ShouldCreatePendingRequest_WithValidData()
    {
        // Arrange & Act
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "{\"text\":\"create card\"}", Guid.NewGuid());

        // Assert
        request.Status.Should().Be(RequestStatus.Pending);
        request.RetryCount.Should().Be(0);
        request.ProcessedAt.Should().BeNull();
        request.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenBoardIdIsEmpty()
    {
        // Act
        var act = () => new LlmRequest(Guid.NewGuid(), "transcript", "payload", Guid.Empty);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Board ID cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void MarkAsFailed_ShouldSetFailureState_AndIncrementRetry()
    {
        // Arrange
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");

        // Act
        request.MarkAsFailed("LLM unavailable");

        // Assert
        request.Status.Should().Be(RequestStatus.Failed);
        request.ErrorMessage.Should().Be("LLM unavailable");
        request.ProcessedAt.Should().NotBeNull();
        request.RetryCount.Should().Be(1);
    }

    [Fact]
    public void ResetForRetry_ShouldSetRequestBackToPending()
    {
        // Arrange
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsFailed("Temporary error");

        // Act
        request.ResetForRetry();

        // Assert
        request.Status.Should().Be(RequestStatus.Pending);
        request.ErrorMessage.Should().BeNull();
        request.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void Cancel_ShouldThrow_WhenRequestIsProcessing()
    {
        // Arrange
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();

        // Act
        var act = () => request.Cancel();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot cancel request that is currently processing")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }
}
