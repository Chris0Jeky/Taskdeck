using System;
using Taskdeck.Acceleration.Candidates.Processing;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.Processing;

public sealed class ProcessingJobStateMachineTests
{
    [Fact]
    public void Expired_running_lease_can_be_reclaimed()
    {
        var now = DateTimeOffset.UtcNow;
        var lease = new CandidateLease("token", "worker", now.AddSeconds(-1), 1);
        Assert.True(ProcessingJobStateMachine.CanRecoverExpiredLease(CandidateProcessingJobState.Running, lease, now));
    }

    [Fact]
    public void Active_running_lease_cannot_be_reclaimed()
    {
        var now = DateTimeOffset.UtcNow;
        var lease = new CandidateLease("token", "worker", now.AddMinutes(1), 1);
        Assert.False(ProcessingJobStateMachine.CanRecoverExpiredLease(CandidateProcessingJobState.Running, lease, now));
    }
}
