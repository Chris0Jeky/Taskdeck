using System.Diagnostics;
using FluentAssertions;
using Taskdeck.Infrastructure.Services;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Behavior tests for <see cref="RedisCacheService"/> when Redis is unreachable.
/// These assert the regression fixed in #1189: a failed connection degrades to the
/// no-cache path without throwing, and the single connect attempt is NOT performed while
/// holding the connection lock (so concurrent callers are not serialized behind a
/// multi-second blocking connect, which previously starved the thread pool).
/// </summary>
public sealed class RedisCacheServiceTests : IDisposable
{
    // Loopback + a port that nothing listens on so Connect fails fast with connection refused.
    // AbortOnConnectFail=false (set by the service) means Connect returns a non-connected
    // multiplexer rather than throwing, exercising the degrade-to-no-cache path.
    private const string UnreachableRedis = "127.0.0.1:1,connectRetry=0,abortConnect=false";

    private readonly InMemoryLogger<RedisCacheService> _logger = new();
    private readonly RedisCacheService _cache;

    public RedisCacheServiceTests()
    {
        _cache = new RedisCacheService(UnreachableRedis, _logger, "test");
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenRedisUnreachable()
    {
        var result = await _cache.GetAsync<TestData>("anykey");

        result.Should().BeNull("an unreachable Redis must degrade to a cache miss, not throw");
    }

    [Fact]
    public async Task SetAsync_DoesNotThrow_WhenRedisUnreachable()
    {
        // Must complete without throwing — writes silently no-op when the cache is down.
        await _cache.SetAsync("key", new TestData("v", 1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenRedisUnreachable()
    {
        await _cache.RemoveAsync("key");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_DoesNotThrow_WhenRedisUnreachable()
    {
        await _cache.RemoveByPrefixAsync("prefix:");
    }

    [Fact]
    public async Task FailedConnect_DegradesToNoCache_AndLogsWarningWithoutThrowing()
    {
        // First call triggers the single connect attempt; it must surface a degraded-mode warning
        // (not an exception) and return the no-cache result.
        var result = await _cache.GetAsync<TestData>("warm");

        result.Should().BeNull();

        var warnings = _logger.Entries
            .Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
            .Select(e => e.Message)
            .ToList();

        warnings.Should().Contain(m => m.Contains("degraded", StringComparison.OrdinalIgnoreCase),
            "a failed connect should log a degraded-mode warning rather than throw");
    }

    [Fact]
    public async Task ConnectionLock_IsNotHeld_WhileBlockingConnectRuns()
    {
        // DETERMINISTIC regression guard for #1189 (does not rely on wall-clock margins).
        //
        // The pre-#1189 code performed the blocking ConnectionMultiplexer.Connect (up to a 3s
        // ConnectTimeout, plus Thread.Sleep backoff) INSIDE lock (_connectionLock). Any other
        // caller that needed the lock was therefore serialized behind the whole connect, starving
        // the thread pool. The fix performs the connect OUTSIDE the lock (the lock guards only the
        // minimal check-and-assign of _connection).
        //
        // We prove this directly: the OnBeforeConnect seam fires on the connecting thread at the
        // exact moment the connect is about to block. From a SEPARATE probe thread we try to take
        // _connectionLock. (TryEnter is re-entrant on the connecting thread, so the probe MUST run
        // on another thread to observe the real held/not-held state.)
        //   - Fixed code:    lock already released -> probe acquires it -> lockWasFree == true.
        //   - Pre-#1189 code: lock held across connect -> probe times out -> lockWasFree == false.
        var probeAcquiredLock = false;
        var probeElapsed = TimeSpan.MaxValue;
        var probeCompleted = new ManualResetEventSlim(false);
        var seamFired = new ManualResetEventSlim(false);

        _cache.OnBeforeConnect = () =>
        {
            // Runs on the connecting thread, immediately before the blocking connect. Hand off to a
            // foreign thread so the lock probe reflects the lock's real state, not re-entrancy.
            seamFired.Set();
            var probe = new Thread(() =>
            {
                // If the connect is OUTSIDE the lock (fixed), this succeeds immediately. If it is
                // INSIDE the lock (old), the connecting thread holds it for the whole connect, so
                // this 2s TryEnter times out (the old code holds the lock for the full 3s+ connect).
                var sw = Stopwatch.StartNew();
                if (Monitor.TryEnter(_cache.ConnectionLock, TimeSpan.FromSeconds(2)))
                {
                    try { probeAcquiredLock = true; }
                    finally { Monitor.Exit(_cache.ConnectionLock); }
                }
                sw.Stop();
                probeElapsed = sw.Elapsed;
                probeCompleted.Set();
            })
            { IsBackground = true, Name = "redis-lock-probe" };
            probe.Start();

            // Block the connecting thread here until the probe has finished its TryEnter window so
            // the observation happens entirely within the connect's hold window. Bounded so the
            // test can never hang.
            probeCompleted.Wait(TimeSpan.FromSeconds(6));
        };

        // Trigger exactly one connect attempt. GetConnection routes through GetDatabase here.
        var result = await _cache.GetAsync<TestData>("trigger");
        result.Should().BeNull();

        seamFired.IsSet.Should().BeTrue("the connect seam must fire — otherwise the test proves nothing");
        probeAcquiredLock.Should().BeTrue(
            "the blocking connect must run OUTSIDE _connectionLock so other callers are not serialized behind it (#1189)");
        // Deterministic timing margin: on the fixed path the lock is free, so the probe acquires it
        // almost instantly (well under 500ms). The old lock-around-connect path would block the
        // probe for the entire ~3s connect, so this assertion fails hard there.
        probeElapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500),
            "a concurrent caller must acquire _connectionLock promptly, not block for the full connect duration");
    }

    [Fact]
    public void Dispose_IsNotSerialized_BehindAnInFlightConnect()
    {
        // Second revert-sensitive guard for #1189, from a real caller's perspective. Dispose() takes
        // lock (_connectionLock) UNCONDITIONALLY — unlike GetAsync, it has no ReconnectMinInterval
        // throttle gate in front of the lock, so it is the cleanest public path that genuinely
        // contends for _connectionLock. (A throttled GetAsync caller would early-return before the
        // lock in BOTH old and new code, so it cannot distinguish the regression — which is exactly
        // why the old wall-clock concurrency test was not a real guard.)
        //
        // While one thread is parked mid-connect (via the OnBeforeConnect seam), Dispose() must
        // acquire the lock and complete promptly.
        //   - Fixed code:    the connecting thread released _connectionLock before the connect, so
        //                    Dispose acquires it immediately.
        //   - Pre-#1189 code: the connecting thread holds _connectionLock across the multi-second
        //                    connect, so Dispose blocks behind it for the whole connect.
        var releaseConnect = new ManualResetEventSlim(false);
        var connectEntered = new ManualResetEventSlim(false);

        _cache.OnBeforeConnect = () =>
        {
            connectEntered.Set();
            // Simulate the multi-second blocking connect. Bounded so the test cannot hang. The
            // fixed code has already released _connectionLock by this point; the old code has not.
            releaseConnect.Wait(TimeSpan.FromSeconds(5));
        };

        // Park a connecting thread inside the seam (simulated mid-connect).
        //
        // Mechanism note (#1332): use a DEDICATED, named background Thread — NOT Task.Run. A
        // Task.Run work item is queued to the thread pool, and under full-API-suite load the pool
        // is saturated; its injection throttle then delays running the item, so the connecting
        // worker could fail to reach OnBeforeConnect before the wait window elapsed (the observed
        // flake: "connectEntered.Wait(5s) ... found False"). A dedicated Thread is created and
        // scheduled by the OS immediately, independent of thread-pool saturation, so the connect
        // seam is reached deterministically. This is a synchronization guarantee, not a bigger
        // timeout — GetConnection invokes OnBeforeConnect synchronously on this thread before any
        // await, so the handshake below is a real rendezvous.
        TestData? connectorResult = null;
        Exception? connectorError = null;
        var connectingThread = new Thread(() =>
        {
            try
            {
                connectorResult = _cache.GetAsync<TestData>("connector").GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                connectorError = ex;
            }
        })
        { IsBackground = true, Name = "redis-connector" };
        connectingThread.Start();

        connectEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "the connect seam must be reached so Dispose is timed against a real mid-connect window");

        // Time Dispose() while the connecting thread is parked mid-connect.
        var sw = Stopwatch.StartNew();
        _cache.Dispose();
        sw.Stop();
        var disposeElapsed = sw.Elapsed;

        // Let the parked connecting thread proceed and finish (it must not throw post-dispose).
        releaseConnect.Set();
        connectingThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "the connecting thread must complete once the simulated connect is released");
        connectorError.Should().BeNull("the parked connector must degrade cleanly post-dispose, not throw");
        connectorResult.Should().BeNull("an unreachable Redis degrades the connector to a miss");

        // On the fixed path Dispose acquires the free lock in microseconds. On the old path it would
        // block ~the full connect (seconds). 500ms is a wide, CI-robust margin that the fixed path
        // clears easily and the serialized path cannot.
        disposeElapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500),
            "Dispose must not be serialized behind another thread's in-flight connect (#1189)");
    }

    [Fact]
    public async Task Dispose_IsIdempotent_AndSubsequentCallsDegradeWithoutThrowing()
    {
        // Establish the degraded state, then dispose. Dispose takes the connection lock, so a
        // concurrent connect cannot leak a multiplexer past disposal; calling it twice must no-op.
        await _cache.GetAsync<TestData>("warm");

        _cache.Dispose();
        _cache.Dispose(); // second call must be a no-op, not throw

        // After disposal the service stays in the no-cache path and never throws.
        var result = await _cache.GetAsync<TestData>("after-dispose");
        result.Should().BeNull();
        await _cache.SetAsync("after-dispose", new TestData("v", 1), TimeSpan.FromMinutes(1));
    }

    private sealed record TestData(string Name, int Value);
}
