using System;
using System.Collections.Generic;

namespace Taskdeck.Acceleration.Candidates.Processing;

public enum CandidateProcessingJobState
{
    Pending,
    Leased,
    Running,
    Retryable,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record CandidateLease(
    string Token,
    string WorkerId,
    DateTimeOffset ExpiresAt,
    int Attempt);

public static class ProcessingJobStateMachine
{
    private static readonly IReadOnlyDictionary<CandidateProcessingJobState, HashSet<CandidateProcessingJobState>> Allowed =
        new Dictionary<CandidateProcessingJobState, HashSet<CandidateProcessingJobState>>
        {
            [CandidateProcessingJobState.Pending] = new() { CandidateProcessingJobState.Leased, CandidateProcessingJobState.Cancelled },
            [CandidateProcessingJobState.Leased] = new() { CandidateProcessingJobState.Running, CandidateProcessingJobState.Retryable, CandidateProcessingJobState.Cancelled },
            [CandidateProcessingJobState.Running] = new() { CandidateProcessingJobState.Succeeded, CandidateProcessingJobState.Failed, CandidateProcessingJobState.Retryable, CandidateProcessingJobState.Cancelled },
            [CandidateProcessingJobState.Retryable] = new() { CandidateProcessingJobState.Leased, CandidateProcessingJobState.Failed, CandidateProcessingJobState.Cancelled },
            [CandidateProcessingJobState.Succeeded] = new(),
            [CandidateProcessingJobState.Failed] = new(),
            [CandidateProcessingJobState.Cancelled] = new()
        };

    public static bool CanTransition(CandidateProcessingJobState from, CandidateProcessingJobState to)
        => Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static void EnsureTransition(CandidateProcessingJobState from, CandidateProcessingJobState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"processing_job_transition_invalid:{from}->{to}");
        }
    }

    public static bool CanClaim(
        CandidateProcessingJobState state,
        CandidateLease? lease,
        DateTimeOffset now)
    {
        return (state is CandidateProcessingJobState.Pending or CandidateProcessingJobState.Retryable)
               && (lease is null || lease.ExpiresAt <= now);
    }

    /// <summary>
    /// An expired active attempt must first transition to Retryable in the same compare-and-swap
    /// transaction; only then may a new lease/attempt be created.
    /// </summary>
    public static bool CanRecoverExpiredLease(
        CandidateProcessingJobState state,
        CandidateLease? lease,
        DateTimeOffset now)
    {
        return (state is CandidateProcessingJobState.Leased or CandidateProcessingJobState.Running)
               && lease is not null
               && lease.ExpiresAt <= now;
    }

    public static bool CanRenew(CandidateLease lease, string token, string workerId, DateTimeOffset now)
    {
        return lease.ExpiresAt > now
               && string.Equals(lease.Token, token, StringComparison.Ordinal)
               && string.Equals(lease.WorkerId, workerId, StringComparison.Ordinal);
    }

    public static CandidateLease CreateLease(
        string token,
        string workerId,
        int nextAttempt,
        DateTimeOffset now,
        TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("lease_token_required", nameof(token));
        if (string.IsNullOrWhiteSpace(workerId)) throw new ArgumentException("worker_id_required", nameof(workerId));
        if (nextAttempt <= 0) throw new ArgumentOutOfRangeException(nameof(nextAttempt));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        return new CandidateLease(token, workerId, now.Add(duration), nextAttempt);
    }
}
