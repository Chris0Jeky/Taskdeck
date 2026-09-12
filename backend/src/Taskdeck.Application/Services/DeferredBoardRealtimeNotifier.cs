namespace Taskdeck.Application.Services;

/// <summary>
/// Bounded transaction bridge for board realtime events staged inside a transaction (#2934).
/// <para>
/// Services publish through <see cref="IBoardRealtimeNotifier"/> right after their own
/// <c>SaveChangesAsync</c>, which is correct for a standalone API write but premature inside the
/// automation executor's outer transaction: a later operation can still fail and roll the write
/// back after subscribers were told it happened. Handing this buffer to a service as its
/// notification sink parks the event instead; the owner of the transaction calls
/// <see cref="FlushAsync"/> after the commit or <see cref="Discard"/> on any path that does not
/// reach it.
/// </para>
/// <para>
/// Deliberately not a general notification redesign: it holds one list, publishes in staging
/// order, and has no knowledge of transactions itself. It is not thread-safe — it is meant to be
/// owned by one scoped, sequential execution.
/// </para>
/// </summary>
public sealed class DeferredBoardRealtimeNotifier : IBoardRealtimeNotifier
{
    private readonly IBoardRealtimeNotifier _inner;
    private readonly List<BoardRealtimeEvent> _pending = new();
    private int _preparedCount;

    public DeferredBoardRealtimeNotifier(IBoardRealtimeNotifier? inner = null)
    {
        _inner = inner ?? NoOpBoardRealtimeNotifier.Instance;
    }

    /// <summary>Events staged but not yet published. Diagnostics and tests only.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Events whose durable channel has been prepared in the caller's transaction.</summary>
    public int PreparedCount => _preparedCount;

    public Task NotifyBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default)
    {
        _pending.Add(mutation);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Prepares the durable channel for every pending event without draining the buffer. Sinks
    /// without the supplemental transactional capability retain the legacy flush behavior.
    /// Successfully prepared events are remembered so a repeated call cannot stage duplicates.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (_inner is not ITransactionalBoardMutationNotifier transactional)
            return;

        while (_preparedCount < _pending.Count)
        {
            await transactional.StageBoardMutationAsync(_pending[_preparedCount], cancellationToken);
            _preparedCount++;
        }
    }

    /// <summary>
    /// Publishes every staged event downstream, in staging order, and empties the buffer.
    /// The buffer is emptied before the first publish, so a downstream failure can never
    /// republish the batch on a later flush.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_pending.Count == 0)
            return;

        var batch = _pending.ToArray();
        var preparedCount = _preparedCount;
        _pending.Clear();
        _preparedCount = 0;
        for (var index = 0; index < batch.Length; index++)
        {
            if (index < preparedCount && _inner is ITransactionalBoardMutationNotifier transactional)
                await transactional.NotifyCommittedBoardMutationAsync(batch[index], cancellationToken);
            else
                await _inner.NotifyBoardMutationAsync(batch[index], cancellationToken);
        }
    }

    /// <summary>Drops every staged event — the write they describe did not survive.</summary>
    public void Discard()
    {
        _pending.Clear();
        _preparedCount = 0;
    }
}
