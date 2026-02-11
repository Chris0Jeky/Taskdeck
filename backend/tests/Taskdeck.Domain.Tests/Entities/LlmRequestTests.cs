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
    public void MarkAsCompleted_ShouldThrow_WhenRequestIsNotProcessing()
    {
        // Arrange
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");

        // Act
        var act = () => request.MarkAsCompleted();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Can only complete requests that are processing")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void MarkAsCompleted_ShouldSetCompletedState_WhenProcessing()
    {
        // Arrange
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();

        // Act
        request.MarkAsCompleted();

        // Assert
        request.Status.Should().Be(RequestStatus.Completed);
        request.ProcessedAt.Should().NotBeNull();
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

    [Fact]
    public void MarkAsProcessing_ShouldThrow_WhenAlreadyProcessing()
    {
        // Arrange
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();

        // Act
        var act = () => request.MarkAsProcessing();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Request is already processing")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Cancel_ShouldThrow_WhenRequestIsCompleted()
    {
        // Arrange
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();
        request.MarkAsCompleted();

        // Act
        var act = () => request.Cancel();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot cancel request that is already completed")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void MarkAsFailed_ShouldThrow_WhenRequestIsCompleted()
    {
        // Arrange
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();
        request.MarkAsCompleted();

        // Act
        var act = () => request.MarkAsFailed("error");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot fail request in Completed status")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }
}
