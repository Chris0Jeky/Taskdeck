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
    public void AttachTranscript_ShouldBeIdempotentForSameId()
    {
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        var transcriptId = Guid.NewGuid();

        request.AttachTranscript(transcriptId);
        request.AttachTranscript(transcriptId);

        request.TranscriptId.Should().Be(transcriptId);
    }

    [Fact]
    public void AttachTranscript_ShouldRejectDifferentId()
    {
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.AttachTranscript(Guid.NewGuid());

        var act = () => request.AttachTranscript(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot attach a different transcript to this request")
            .Where(exception => exception.ErrorCode == ErrorCodes.Conflict);
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
    public void ReleaseClaim_ShouldReturnToPending_WithoutChargingRetryBudget()
    {
        // #1605: a claim abandoned by a graceful shutdown is not a failed attempt, so the request goes
        // back on the queue with its retry budget intact -- unlike MarkAsFailed + ResetForRetry.
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();

        request.ReleaseClaim();

        request.Status.Should().Be(RequestStatus.Pending);
        request.RetryCount.Should().Be(0);
    }

    [Fact]
    public void ReleaseClaim_ShouldPreserveAnAlreadyConsumedRetryBudget()
    {
        // A request on its second attempt keeps the retry it already spent: releasing a claim neither
        // charges nor refunds.
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();
        request.MarkAsFailed("Temporary error");
        request.ResetForRetry();
        request.MarkAsProcessing();

        request.ReleaseClaim();

        request.Status.Should().Be(RequestStatus.Pending);
        request.RetryCount.Should().Be(1);
    }

    [Theory]
    [InlineData(RequestStatus.Pending)]
    [InlineData(RequestStatus.Completed)]
    [InlineData(RequestStatus.Failed)]
    public void ReleaseClaim_ShouldThrow_WhenRequestIsNotProcessing(RequestStatus status)
    {
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        switch (status)
        {
            case RequestStatus.Completed:
                request.MarkAsProcessing();
                request.MarkAsCompleted();
                break;
            case RequestStatus.Failed:
                request.MarkAsProcessing();
                request.MarkAsFailed("error");
                break;
        }

        request.Status.Should().Be(status);

        var act = () => request.ReleaseClaim();

        act.Should().Throw<DomainException>()
            .WithMessage("Can only release the claim on a processing request")
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

    [Fact]
    public void MarkAsCompleted_ShouldRecordDegradedNotice_WithoutFailingOrRetryingTheRequest()
    {
        // #2192: a capture whose LLM triage leg failed still produced a reviewable proposal from
        // the deterministic extractor. It must complete — not fail, not burn a retry — while
        // carrying the record of which engine actually produced the result.
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();

        request.MarkAsCompleted("LLM triage unavailable (ProviderDegraded); using deterministic extractor");

        request.Status.Should().Be(RequestStatus.Completed);
        request.ErrorMessage.Should()
            .Be("LLM triage unavailable (ProviderDegraded); using deterministic extractor");
        request.RetryCount.Should().Be(0);
        request.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkAsCompleted_ShouldLeaveNoNotice_WhenNoneIsSupplied()
    {
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();

        request.MarkAsCompleted();

        request.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarkAsCompleted_ShouldNotCarryAMessageForwardFromAFailedAttempt()
    {
        // Walks the real retry lifecycle rather than calling MarkAsCompleted in isolation, which
        // is the only way to observe whether a clean completion can inherit an earlier failure's
        // message. ResetForRetry is the sole route from Failed back to Processing and it already
        // clears ErrorMessage, so a request is provably always clean by the time it completes —
        // the assignment in MarkAsCompleted is defence in depth, not the thing doing the work.
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();
        request.MarkAsFailed("Transient provider error");
        request.ErrorMessage.Should().Be("Transient provider error");

        request.ResetForRetry();
        request.MarkAsProcessing();
        request.MarkAsCompleted();

        request.Status.Should().Be(RequestStatus.Completed);
        request.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarkAsCompleted_ShouldRecordADegradedNotice_OnARetryThatFollowedAFailure()
    {
        // The same lifecycle, but the retry degrades: the notice from THIS run must land, and it
        // must not be confused with the previous attempt's failure message.
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();
        request.MarkAsFailed("Transient provider error");
        request.ResetForRetry();
        request.MarkAsProcessing();

        request.MarkAsCompleted("LLM triage unavailable (QuotaExceeded); using deterministic extractor");

        request.Status.Should().Be(RequestStatus.Completed);
        request.ErrorMessage.Should().Be("LLM triage unavailable (QuotaExceeded); using deterministic extractor");
        request.ErrorMessage.Should().NotContain("Transient provider error");
    }

    [Fact]
    public void MarkAsCompleted_ShouldTreatWhitespaceNoticeAsAbsent()
    {
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();

        request.MarkAsCompleted("   ");

        request.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarkAsCompleted_ShouldTruncateANoticeToThePersistedColumnBound()
    {
        var request = new LlmRequest(Guid.NewGuid(), "transcript", "payload");
        request.MarkAsProcessing();

        request.MarkAsCompleted(new string('x', LlmRequest.MaxErrorMessageLength + 250));

        request.ErrorMessage.Should().HaveLength(LlmRequest.MaxErrorMessageLength);
    }
}
