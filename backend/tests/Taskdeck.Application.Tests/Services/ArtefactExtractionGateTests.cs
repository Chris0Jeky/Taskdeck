using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Covers the <see cref="ArtefactExtractionGate"/> permit semantics wired into
/// <see cref="ArtefactExtractionService"/> for #1379: an abandoned parse holds its
/// permit until it actually finishes, saturation rejects with
/// <c>TooManyRequests</c> and writes no history row, and concurrency is capped
/// (never queued). All synchronization is mechanism-based (latches +
/// TaskCompletionSource); no sleeps or timing assumptions decide outcomes.
/// </summary>
public sealed class ArtefactExtractionGateTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _artefactId = Guid.NewGuid();
    private readonly Mock<ISourceArtefactRepository> _artefacts = new();
    private readonly Mock<IArtefactExtractionRepository> _extractions = new();

    [Fact]
    public async Task AbandonedParse_HoldsPermit_UntilItFinishes_ThenCapacityFrees()
    {
        ArrangeStoredArtefact("application/pdf", [1, 2, 3]);
        var storeCalls = 0;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref storeCalls))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);

        var gate = new ArtefactExtractionGate(new ArtefactStorageSettings { ExtractionMaxConcurrency = 1 });
        var permitReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.PermitReleased += () => permitReleased.TrySetResult();

        using var block1 = new ManualResetEventSlim(false);
        var tinyBudget = new ArtefactStorageSettings { ExtractionTimeoutSeconds = 0.05, ExtractionMaxConcurrency = 1 };

        // Submission 1: an extractor that ignores cancellation (models PdfPig's
        // synchronous Open). The tiny budget fires, the request is abandoned, but the
        // worker stays blocked on block1 — so the single permit is still held.
        var blockingExtractor = new BlockingExtractor("application/pdf", block1);
        var service1 = new ArtefactExtractionService(
            _artefacts.Object, _extractions.Object, [blockingExtractor], tinyBudget, gate: gate);

        var first = await service1.ExtractAsync(_userId, _artefactId);
        first.IsSuccess.Should().BeTrue();
        first.Value.Warnings.Should().Equal(ArtefactExtractionWarningCodes.ExtractionTimeout);
        gate.AvailablePermits.Should().Be(0, "the abandoned worker is still blocked and holds the permit");

        // Submission 2: the permit is provably still held (block1 is not set, so the
        // worker cannot have finished). It must be rejected pre-parse with no row.
        var service2 = new ArtefactExtractionService(
            _artefacts.Object, _extractions.Object, [new BlockingExtractor("application/pdf", block1)], tinyBudget, gate: gate);
        var second = await service2.ExtractAsync(_userId, _artefactId);
        second.IsSuccess.Should().BeFalse();
        second.ErrorCode.Should().Be(ErrorCodes.TooManyRequests);
        storeCalls.Should().Be(1, "the rejected submission writes no extraction-history row");

        // Release the abandoned worker; the service's completion continuation now runs
        // and returns the permit. Await that deterministically via the gate's hook.
        block1.Set();
        (await Task.WhenAny(permitReleased.Task, Task.Delay(TimeSpan.FromSeconds(10))))
            .Should().Be(permitReleased.Task, "the abandoned worker must release its permit once it finishes");
        gate.AvailablePermits.Should().Be(1);

        // Submission 3: capacity is free again, so a normal extraction succeeds and
        // writes a row. A generous budget avoids any interaction with the abandonment.
        var fastExtractor = new BlockingExtractor(
            "application/pdf",
            block: null,
            result: new ArtefactExtractionResult("recovered", [], "Blocking", "1.0"));
        var service3 = new ArtefactExtractionService(
            _artefacts.Object,
            _extractions.Object,
            [fastExtractor],
            new ArtefactStorageSettings { ExtractionTimeoutSeconds = 30, ExtractionMaxConcurrency = 1 },
            gate: gate);
        var third = await service3.ExtractAsync(_userId, _artefactId);
        third.IsSuccess.Should().BeTrue();
        third.Value.ExtractedText.Should().Be("recovered");
        storeCalls.Should().Be(2);
        gate.AvailablePermits.Should().Be(1, "the successful extraction released its permit");
    }

    [Fact]
    public async Task Saturated_RejectsExcess_NeverQueues_AndNeverExceedsCap()
    {
        ArrangeStoredArtefact("application/pdf", [1, 2, 3]);
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);

        const int cap = 2;
        const int submissions = 8;
        var gate = new ArtefactExtractionGate(new ArtefactStorageSettings { ExtractionMaxConcurrency = cap });
        using var release = new ManualResetEventSlim(false);
        using var allEntered = new CountdownEvent(cap);
        var latched = new LatchedCountingExtractor("application/pdf", release, allEntered);

        // A generous budget: the two admitted workers block on the latch and then
        // complete normally (never abandoned), so the outcome is a clean 2 success /
        // 6 rejected split with no queueing.
        var settings = new ArtefactStorageSettings { ExtractionTimeoutSeconds = 30, ExtractionMaxConcurrency = cap };
        var service = new ArtefactExtractionService(
            _artefacts.Object, _extractions.Object, [latched], settings, gate: gate);

        var tasks = Enumerable
            .Range(0, submissions)
            .Select(_ => service.ExtractAsync(_userId, _artefactId))
            .ToArray();

        // Exactly cap workers can enter the extractor concurrently; the rest are
        // rejected immediately. Deterministic: the two that entered block on the latch,
        // so no permit ever frees to admit a third.
        allEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("exactly the cap should enter the extractor");
        latched.MaxConcurrent.Should().Be(cap);
        gate.AvailablePermits.Should().Be(0);

        release.Set();
        var all = Task.WhenAll(tasks);
        var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(15)));
        completed.Should().Be(all, "no deadlock: every submission resolves after release");

        var results = await all;
        results.Count(r => r.IsSuccess).Should().Be(cap);
        results.Count(r => !r.IsSuccess && r.ErrorCode == ErrorCodes.TooManyRequests)
            .Should().Be(submissions - cap);
        latched.MaxConcurrent.Should().Be(cap, "the gate never let more than the cap run at once");
        gate.AvailablePermits.Should().Be(cap, "every permit was returned");
    }

    [Fact]
    public async Task CallerCancellation_AbandonsWorker_RecordsNoRow_AndReleasesPermitWhenWorkerFinishes()
    {
        // The caller-cancellation branch is distinct from the budget-timeout branch: it
        // rethrows (no history row) but still defers permit release to the worker-completion
        // continuation. Pin that the permit is held until the abandoned worker finishes and
        // released exactly once thereafter, with no extraction-history row written.
        ArrangeStoredArtefact("application/pdf", [1, 2, 3]);
        var storeCalls = 0;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref storeCalls))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);

        var gate = new ArtefactExtractionGate(new ArtefactStorageSettings { ExtractionMaxConcurrency = 1 });
        var permitReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.PermitReleased += () => permitReleased.TrySetResult();

        using var release = new ManualResetEventSlim(false);
        using var entered = new CountdownEvent(1);
        // A generous budget so only caller cancellation (never the timeout branch) can end
        // the wait; the latched extractor ignores its token, so the parse must be abandoned.
        var settings = new ArtefactStorageSettings { ExtractionTimeoutSeconds = 30, ExtractionMaxConcurrency = 1 };
        var extractor = new LatchedCountingExtractor("application/pdf", release, entered);
        var service = new ArtefactExtractionService(
            _artefacts.Object, _extractions.Object, [extractor], settings, gate: gate);

        using var callerCts = new CancellationTokenSource();
        var extraction = service.ExtractAsync(_userId, _artefactId, callerCts.Token);

        entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the worker must enter the extractor before we cancel");
        gate.AvailablePermits.Should().Be(0, "the running worker holds the permit");

        callerCts.Cancel();

        var act = async () => await extraction;
        await act.Should().ThrowAsync<OperationCanceledException>();
        storeCalls.Should().Be(0, "a caller-cancelled extraction writes no history row");
        gate.AvailablePermits.Should().Be(0, "the abandoned worker still holds the permit until it finishes");

        release.Set();
        (await Task.WhenAny(permitReleased.Task, Task.Delay(TimeSpan.FromSeconds(10))))
            .Should().Be(permitReleased.Task, "the abandoned worker must release its permit once it finishes");
        gate.AvailablePermits.Should().Be(1, "the permit is returned exactly once after the abandoned worker completes");
    }

    private void ArrangeStoredArtefact(string mimeType, byte[] content)
    {
        var artefact = new SourceArtefact(
            _userId,
            ArtefactKind.Pdf,
            mimeType,
            "source.pdf",
            content.LongLength,
            new string('a', 64),
            CaptureSource.Import);
        typeof(Taskdeck.Domain.Common.Entity)
            .GetProperty(nameof(Taskdeck.Domain.Common.Entity.Id))!
            .SetValue(artefact, _artefactId);

        _artefacts
            .Setup(repository => repository.GetByIdForUserAsync(
                _artefactId,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(artefact);
        _artefacts
            .Setup(repository => repository.CopyContentForUserAsync(
                _artefactId,
                _userId,
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, Guid _, Stream destination, CancellationToken cancellationToken) =>
            {
                await destination.WriteAsync(content, cancellationToken);
                return true;
            });
    }

    /// <summary>
    /// Ignores the cancellation token entirely (models PdfPig's synchronous
    /// <c>PdfDocument.Open</c>) and optionally blocks on a latch, so the service must
    /// abandon it when the budget fires.
    /// </summary>
    private sealed class BlockingExtractor : IArtefactTextExtractor
    {
        private readonly string _mime;
        private readonly ManualResetEventSlim? _block;
        private readonly ArtefactExtractionResult _result;

        public BlockingExtractor(string mime, ManualResetEventSlim? block, ArtefactExtractionResult? result = null)
        {
            _mime = mime;
            _block = block;
            _result = result ?? new ArtefactExtractionResult("blocked", [], "Blocking", "1.0");
        }

        public string ExtractorName => "Blocking";
        public string ExtractorVersion => "1.0";
        public long InputByteLimit => 1024 * 1024;

        public bool CanExtract(string mimeType)
            => mimeType.StartsWith(_mime, StringComparison.OrdinalIgnoreCase);

        public Task<ArtefactExtractionResult> ExtractAsync(Stream content, CancellationToken cancellationToken = default)
        {
            _block?.Wait(TimeSpan.FromSeconds(30));
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// Records the peak number of concurrent extractor entries, signals each entry,
    /// then blocks all entrants on a shared latch — proving the gate admits at most
    /// the configured cap at once.
    /// </summary>
    private sealed class LatchedCountingExtractor : IArtefactTextExtractor
    {
        private readonly string _mime;
        private readonly ManualResetEventSlim _release;
        private readonly CountdownEvent _entered;
        private int _current;
        private int _max;

        public LatchedCountingExtractor(string mime, ManualResetEventSlim release, CountdownEvent entered)
        {
            _mime = mime;
            _release = release;
            _entered = entered;
        }

        public string ExtractorName => "LatchedCounting";
        public string ExtractorVersion => "1.0";
        public long InputByteLimit => 1024 * 1024;
        public int MaxConcurrent => Volatile.Read(ref _max);

        public bool CanExtract(string mimeType)
            => mimeType.StartsWith(_mime, StringComparison.OrdinalIgnoreCase);

        public Task<ArtefactExtractionResult> ExtractAsync(Stream content, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _current);
            int observed;
            do
            {
                observed = Volatile.Read(ref _max);
                if (now <= observed)
                    break;
            }
            while (Interlocked.CompareExchange(ref _max, now, observed) != observed);

            _entered.Signal();
            _release.Wait(TimeSpan.FromSeconds(30));
            Interlocked.Decrement(ref _current);
            return Task.FromResult(new ArtefactExtractionResult("done", [], ExtractorName, ExtractorVersion));
        }
    }
}
