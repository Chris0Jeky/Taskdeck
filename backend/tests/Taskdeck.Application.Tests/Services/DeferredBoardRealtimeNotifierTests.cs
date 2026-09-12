using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Unit cover for the #2934 transaction bridge itself: it must hold everything back until the
/// owner of the transaction flushes, publish in staging order, never republish a flushed batch,
/// and forget everything on discard.
/// </summary>
public class DeferredBoardRealtimeNotifierTests
{
    [Fact]
    public async Task Notify_ShouldStageWithoutPublishing_UntilFlush()
    {
        var inner = new CollectingNotifier();
        var deferred = new DeferredBoardRealtimeNotifier(inner);

        await deferred.NotifyBoardMutationAsync(Event("archived"));
        await deferred.NotifyBoardMutationAsync(Event("updated"));

        inner.Published.Should().BeEmpty();
        deferred.PendingCount.Should().Be(2);

        await deferred.FlushAsync();

        inner.Published.Select(e => e.Operation).Should().Equal("archived", "updated");
        deferred.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task Discard_ShouldDropStagedEvents()
    {
        var inner = new CollectingNotifier();
        var deferred = new DeferredBoardRealtimeNotifier(inner);

        await deferred.NotifyBoardMutationAsync(Event("archived"));
        deferred.Discard();
        await deferred.FlushAsync();

        inner.Published.Should().BeEmpty();
        deferred.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task Flush_ShouldNotRepublish_WhenCalledAgain()
    {
        var inner = new CollectingNotifier();
        var deferred = new DeferredBoardRealtimeNotifier(inner);

        await deferred.NotifyBoardMutationAsync(Event("archived"));
        await deferred.FlushAsync();
        await deferred.FlushAsync();

        inner.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task Flush_ShouldNotRepublish_WhenTheDownstreamChannelThrows()
    {
        var inner = new ThrowingNotifier();
        var deferred = new DeferredBoardRealtimeNotifier(inner);

        await deferred.NotifyBoardMutationAsync(Event("archived"));
        var flush = async () => await deferred.FlushAsync();
        await flush.Should().ThrowAsync<InvalidOperationException>();

        deferred.PendingCount.Should().Be(0);
        inner.Attempts.Should().Be(1);

        await deferred.FlushAsync();
        inner.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Notify_ShouldNotThrow_WhenNoDownstreamNotifierIsConfigured()
    {
        var deferred = new DeferredBoardRealtimeNotifier(null);

        await deferred.NotifyBoardMutationAsync(Event("archived"));
        await deferred.FlushAsync();

        deferred.PendingCount.Should().Be(0);
    }

    private static BoardRealtimeEvent Event(string operation)
        => new(Guid.NewGuid(), "card", operation, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private sealed class CollectingNotifier : IBoardRealtimeNotifier
    {
        public List<BoardRealtimeEvent> Published { get; } = new();

        public Task NotifyBoardMutationAsync(BoardRealtimeEvent mutation, CancellationToken cancellationToken = default)
        {
            Published.Add(mutation);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingNotifier : IBoardRealtimeNotifier
    {
        public int Attempts { get; private set; }

        public Task NotifyBoardMutationAsync(BoardRealtimeEvent mutation, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("channel down");
        }
    }
}
