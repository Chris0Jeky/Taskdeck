using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class AudioTranscriptionTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static AudioTranscriptionAttempt Attempt() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), new string('a', 64), new string('b', 64), "Configured speech", "fixture", Started, Started.AddSeconds(60));

    [Fact]
    public void ExpiredWorkCannotPublishAndRetryRequiresAnotherReceipt()
    {
        var attempt = Attempt();
        attempt.Expire(Started.AddSeconds(59)).Should().BeFalse();
        attempt.Expire(Started.AddSeconds(60)).Should().BeTrue();
        attempt.State.Should().Be(ProcessingJobState.Expired);
        attempt.FailureCode.Should().Be("interrupted");
        attempt.RepresentationId.Should().BeNull();
        var complete = () => attempt.Complete(Guid.NewGuid(), Started.AddSeconds(61));
        complete.Should().Throw<DomainException>();
        attempt.Expire(Started.AddMinutes(3)).Should().BeFalse();
    }

    [Fact]
    public void CompletedCandidateIsFinalAndFailuresContainOnlyKnownCodes()
    {
        var attempt = Attempt(); var representation = Guid.NewGuid();
        attempt.Complete(representation, Started.AddSeconds(20));
        attempt.State.Should().Be(ProcessingJobState.Completed);
        attempt.RepresentationId.Should().Be(representation);
        var fail = () => attempt.Fail("provider-timeout", Started.AddSeconds(21));
        fail.Should().Throw<DomainException>();
        var other = Attempt();
        var rawError = () => other.Fail("raw provider text or credentials", Started.AddSeconds(10));
        rawError.Should().Throw<DomainException>();
        other.Fail("provider-response", Started.AddSeconds(10));
        other.State.Should().Be(ProcessingJobState.Failed);
        other.RepresentationId.Should().BeNull();
    }

    [Fact]
    public void AdmissionIsBoundedAcrossRequestsAndCannotRewindItsUtcDay()
    {
        var budget = new AudioTranscriptionBudget(Guid.NewGuid());
        budget.Reserve(60, 2, 100, Started).Should().BeTrue();
        budget.Reserve(41, 2, 100, Started).Should().BeFalse();
        budget.Reserve(40, 2, 100, Started).Should().BeTrue();
        budget.Reserve(1, 2, 100, Started).Should().BeFalse();
        budget.Revision.Should().Be(2);
        budget.Reserve(100, 2, 100, Started.AddDays(1)).Should().BeTrue();
        budget.Attempts.Should().Be(1);
        budget.Reserve(1, 2, 100, Started).Should().BeFalse();
        budget.InputBytes.Should().Be(100);
        budget.Revision.Should().Be(3);
    }

    [Fact]
    public void AdmissionUsesUtcAndRejectsAnOversizedFirstRequestWithoutResettingState()
    {
        var budget = new AudioTranscriptionBudget(Guid.NewGuid());
        budget.Reserve(long.MaxValue, 2, 100, Started).Should().BeFalse();
        budget.UtcDay.Should().Be(0);
        budget.Reserve(20, 2, 100, Started.ToOffset(TimeSpan.FromHours(-12))).Should().BeTrue();
        budget.UtcDay.Should().Be(20260910);
        budget.Attempts.Should().Be(1);
    }
}
