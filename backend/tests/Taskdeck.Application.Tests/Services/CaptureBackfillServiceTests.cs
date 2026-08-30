using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;
using Capture = Taskdeck.Domain.Entities.Capture;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// CF-01 (#2255): the ID-preserving backfill and reconcile pass. It must be idempotent, resumable,
/// derive the three state axes from what each row recorded, store the material as immutable source
/// assets, bring a diverged aggregate back into agreement with its queue row, step over a row it
/// cannot map instead of stalling behind it, and never claim completion while anything is
/// outstanding.
/// </summary>
public sealed class CaptureBackfillServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly FakeCaptureStore _captureStore = new();
    private readonly FakeBackfillStore _backfillStore;
    private readonly Guid _userId = Guid.NewGuid();

    public CaptureBackfillServiceTests()
    {
        _backfillStore = new FakeBackfillStore(_captureStore);
    }

    private CaptureBackfillService CreateService(bool backfill = true) =>
        new(
            _unitOfWork.Object,
            _captureStore,
            _backfillStore,
            new ContextFabricSettings { BackfillCaptures = backfill });

    private LlmRequest SeedLegacyRow(
        CaptureSource source = CaptureSource.Typed,
        string text = "book the venue",
        RequestStatus status = RequestStatus.Pending,
        CaptureDispositionV1? disposition = null,
        CaptureProvenanceV1? provenance = null,
        string? externalRef = null,
        string? titleHint = null,
        Guid? boardId = null)
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            source,
            text,
            TitleHint: titleHint,
            ExternalRef: externalRef,
            Provenance: provenance,
            Disposition: disposition);
        return SeedRawRow(
            CaptureRequestContract.SerializePayload(payload),
            CaptureRequestContract.ResolveRequestTypeForSource(source),
            status,
            boardId);
    }

    /// <summary>
    /// A row whose stored payload is whatever the caller says. Used for the poisoned rows: a legacy
    /// row whose payload is not valid capture JSON falls back to raw text, and a raw text longer than
    /// a source asset may carry cannot be mapped at all.
    /// </summary>
    private LlmRequest SeedRawRow(
        string payload,
        string requestType = CaptureRequestContract.RequestTypeV1,
        RequestStatus status = RequestStatus.Pending,
        Guid? boardId = null)
    {
        var request = new LlmRequest(_userId, requestType, payload, boardId);
        switch (status)
        {
            case RequestStatus.Processing:
                request.MarkAsProcessing();
                break;
            case RequestStatus.Completed:
                request.MarkAsProcessing();
                request.MarkAsCompleted();
                break;
            case RequestStatus.Failed:
                request.MarkAsProcessing();
                request.MarkAsFailed("boom");
                break;
            case RequestStatus.Cancelled:
                request.Cancel();
                break;
        }

        _backfillStore.Add(request);
        return request;
    }

    private LlmRequest SeedUnmappableRow() =>
        SeedRawRow(new string('x', SourceAsset.MaxInlineTextLength + 1));

    [Fact]
    public async Task RunAsync_ShouldMigrateEveryLegacyRowUnderItsOwnId()
    {
        var first = SeedLegacyRow(text: "first");
        var second = SeedLegacyRow(text: "second");

        var result = await CreateService().RunAsync();

        result.Migrated.Should().Be(2);
        result.Remaining.Should().Be(0);
        result.Complete.Should().BeTrue();
        _captureStore.All.Select(capture => capture.Id).Should().BeEquivalentTo(new[] { first.Id, second.Id });
        _captureStore.All.Should().OnlyContain(capture => capture.LegacyRequestId == capture.Id);
        _captureStore.All.Should().OnlyContain(capture => capture.UserId == _userId);
    }

    [Fact]
    public async Task RunAsync_ShouldBeIdempotent()
    {
        SeedLegacyRow();
        var service = CreateService();

        var first = await service.RunAsync();
        var second = await service.RunAsync();

        first.Migrated.Should().Be(1);
        second.Migrated.Should().Be(0, "a row that agrees with its capture leaves the backlog");
        second.Reconciled.Should().Be(0, "and it is not re-examined either");
        _captureStore.All.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ShouldResumeAcrossBatchesAndAcrossRuns()
    {
        for (var index = 0; index < 5; index++)
        {
            SeedLegacyRow(text: $"row {index}");
        }

        var interrupted = await CreateService().RunAsync(batchSize: 2);
        _backfillStore.SimulateCrashAfter(interrupted.Migrated - 1);

        var resumed = await CreateService().RunAsync(batchSize: 2);

        _captureStore.All.Select(capture => capture.Id).Distinct().Should().HaveCount(5, "nothing is created twice");
        resumed.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ShouldStoreTheTextAsAnImmutableInlineAssetAndTheLocatorAsAnExternalReference()
    {
        SeedLegacyRow(
            source: CaptureSource.WebClip,
            text: "the clipped article",
            externalRef: "https://example.test/article");

        await CreateService().RunAsync();

        var capture = _captureStore.All.Should().ContainSingle().Subject;
        capture.SourceAssets.Should().HaveCount(2);
        capture.SourceAssets[0].StorageKind.Should().Be(SourceAssetStorageKind.InlineText);
        capture.SourceAssets[0].TextPayload!.Text.Should().Be("the clipped article");
        capture.SourceAssets[1].StorageKind.Should().Be(SourceAssetStorageKind.ExternalReference);
        capture.SourceAssets[1].ExternalReference.Should().Be("https://example.test/article");
        capture.CurrentText.Should().Be("the clipped article");
    }

    [Theory]
    [InlineData(RequestStatus.Pending, false, CaptureProcessingSummary.Idle, CaptureActionState.Unplanned, CaptureTimelineStep.Received)]
    [InlineData(RequestStatus.Processing, false, CaptureProcessingSummary.Processing, CaptureActionState.Unplanned, CaptureTimelineStep.Preparing)]
    [InlineData(RequestStatus.Completed, false, CaptureProcessingSummary.Ready, CaptureActionState.Unplanned, CaptureTimelineStep.Understood)]
    [InlineData(RequestStatus.Completed, true, CaptureProcessingSummary.Ready, CaptureActionState.NeedsReview, CaptureTimelineStep.NeedsReview)]
    [InlineData(RequestStatus.Failed, false, CaptureProcessingSummary.Failed, CaptureActionState.Unplanned, CaptureTimelineStep.Failed)]
    public async Task RunAsync_ShouldDeriveTheStateAxesFromTheQueueRowRatherThanDefaultingToReceived(
        RequestStatus status,
        bool withProposal,
        CaptureProcessingSummary expectedProcessing,
        CaptureActionState expectedAction,
        CaptureTimelineStep expectedTimeline)
    {
        var requestId = Guid.NewGuid();
        SeedLegacyRow(
            status: status,
            provenance: withProposal
                ? new CaptureProvenanceV1(requestId, ProposalId: Guid.NewGuid())
                : null);

        await CreateService().RunAsync();

        var capture = _captureStore.All.Should().ContainSingle().Subject;
        capture.ProcessingSummary.Should().Be(expectedProcessing);
        capture.ActionState.Should().Be(expectedAction);
        capture.Timeline.Should().Be(expectedTimeline);
    }

    [Fact]
    public async Task RunAsync_ShouldCarryAKeptDispositionAndItsRememberIntent()
    {
        SeedLegacyRow(disposition: new CaptureDispositionV1(CaptureDisposition.Kept, DateTimeOffset.UtcNow, _userId));

        await CreateService().RunAsync();

        var capture = _captureStore.All.Should().ContainSingle().Subject;
        capture.Disposition.Should().Be(CaptureUserDisposition.Kept);
        capture.RequestedIntent.Should().Be(CaptureIntentMode.Remember);
        capture.Timeline.Should().Be(CaptureTimelineStep.Kept);
    }

    [Fact]
    public async Task RunAsync_ShouldKeepAnAppliedOutcomeOnAnArchivedRow()
    {
        var requestId = Guid.NewGuid();
        SeedLegacyRow(
            status: RequestStatus.Cancelled,
            disposition: new CaptureDispositionV1(CaptureDisposition.Archived, DateTimeOffset.UtcNow, _userId),
            provenance: new CaptureProvenanceV1(
                requestId,
                ProposalId: Guid.NewGuid(),
                ConvertedAt: DateTimeOffset.UtcNow));

        await CreateService().RunAsync();

        var capture = _captureStore.All.Should().ContainSingle().Subject;
        capture.Disposition.Should().Be(CaptureUserDisposition.Archived);
        capture.ActionState.Should().Be(CaptureActionState.Acted, "archiving does not erase what was applied");
        capture.ProcessingSummary.Should().Be(CaptureProcessingSummary.Ready);
        capture.Timeline.Should().Be(CaptureTimelineStep.Archived);
    }

    // ---------------------------------------------------------------- HIGH-2: a poisoned head
    [Fact]
    public async Task RunAsync_ShouldStepOverAWholeBatchOfUnmappableRowsAndStillReachTheHealthyOnesBehindThem()
    {
        // Round-1 review, reproduced: three unmappable rows sit at the head of the oldest-first
        // backlog and a batch holds exactly three. Re-reading the same head each iteration stalled
        // the pass on every start while the marker still claimed completion.
        var poisoned = new[] { SeedUnmappableRow(), SeedUnmappableRow(), SeedUnmappableRow() };
        var healthy = Enumerable.Range(0, 5)
            .Select(index => SeedLegacyRow(text: $"healthy {index}"))
            .ToArray();

        var result = await CreateService().RunAsync(batchSize: 3);

        result.Migrated.Should().Be(5, "every healthy row behind the poisoned head is still reached");
        result.Skipped.Should().Be(3);
        result.Remaining.Should().Be(3, "only the unmappable rows are left");
        result.Complete.Should().BeFalse("the marker must not claim a drained backlog while rows are outstanding");
        _captureStore.All.Select(capture => capture.Id).Should().BeEquivalentTo(healthy.Select(row => row.Id));
        _captureStore.All.Select(capture => capture.Id).Should().NotIntersectWith(poisoned.Select(row => row.Id));
        _backfillStore.SavedState!.IsComplete.Should().BeFalse();
        _backfillStore.SavedState.SkippedCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_ShouldCountASkippedRowOnceNoMatterHowManyTimesItIsRetried()
    {
        SeedUnmappableRow();
        SeedLegacyRow(text: "healthy");
        var service = CreateService();

        await service.RunAsync(batchSize: 1);
        await service.RunAsync(batchSize: 1);
        var third = await service.RunAsync(batchSize: 1);

        third.Skipped.Should().Be(1);
        _backfillStore.SavedState!.SkippedCount.Should().Be(1, "the snapshot counts rows, not attempts");
        _backfillStore.SavedState.MigratedCount.Should().Be(1, "and the healthy row is only brought in once");
    }

    // ---------------------------------------------------------------- HIGH-1: divergence
    [Fact]
    public async Task RunAsync_ShouldReconcileACaptureWhoseQueueRowWasEditedWhileDualWriteWasOff()
    {
        // Round-1 review, reproduced: with dual-write off the edit lands only on the queue row. An
        // anti-join would report nothing to do, and the read switch would then serve the pre-edit
        // text forever. The divergence join has to notice and re-supersede.
        var request = SeedLegacyRow(text: "first draft");
        await CreateService().RunAsync();
        _captureStore.All.Should().ContainSingle().Which.CurrentText.Should().Be("first draft");

        EditQueuePayloadOnly(request, "corrected draft");

        var result = await CreateService().RunAsync();

        result.Migrated.Should().Be(0, "the capture already exists");
        result.Reconciled.Should().Be(1);
        result.Remaining.Should().Be(0);
        result.Complete.Should().BeTrue();

        var capture = _captureStore.All.Should().ContainSingle().Subject;
        capture.CurrentText.Should().Be("corrected draft");
        capture.SourceAssets.Should().HaveCount(2, "the correction supersedes, it never rewrites");
        capture.SourceAssets[0].TextPayload!.Text.Should().Be("first draft");
        capture.SourceAssets[0].IsActive.Should().BeFalse();
        capture.SourceAssets[1].SupersedesAssetId.Should().Be(capture.SourceAssets[0].Id);
    }

    [Fact]
    public async Task RunAsync_ShouldReconcileATitleAndAnExternalReferenceAddedAfterTheCaptureWasBuilt()
    {
        var request = SeedLegacyRow(text: "clip me");
        await CreateService().RunAsync();

        EditQueuePayloadOnly(
            request,
            "clip me",
            titleHint: "Renamed",
            externalRef: "https://example.test/added-later");

        await CreateService().RunAsync();

        var capture = _captureStore.All.Should().ContainSingle().Subject;
        capture.UserTitle.Should().Be("Renamed");
        capture.ActiveSourceAssets.Should().Contain(asset =>
            asset.StorageKind == SourceAssetStorageKind.ExternalReference &&
            asset.ExternalReference == "https://example.test/added-later");
    }

    [Fact]
    public async Task RunAsync_ShouldStampACaptureThatNeededNoChangeSoItLeavesTheBacklog()
    {
        // Without the stamp a row whose queue UpdatedAt merely moved on (a triage transition, say)
        // would be re-examined on every single start, forever.
        var request = SeedLegacyRow(text: "unchanged");
        await CreateService().RunAsync();
        request.MarkAsProcessing();

        var first = await CreateService().RunAsync();
        var second = await CreateService().RunAsync();

        first.Reconciled.Should().Be(1);
        second.Reconciled.Should().Be(0, "the reconciliation stamp took the capture out of the backlog");
        second.Remaining.Should().Be(0);
        _captureStore.All.Should().ContainSingle().Which.ProcessingSummary
            .Should().Be(CaptureProcessingSummary.Processing, "the axes follow the queue row too");
    }

    [Fact]
    public async Task RunAsync_WithTheFlagOff_ShouldDoNothingAndLeaveTheMarkerIncomplete()
    {
        SeedLegacyRow();

        var result = await CreateService(backfill: false).RunAsync();

        result.Ran.Should().BeFalse();
        result.Complete.Should().BeFalse();
        _captureStore.All.Should().BeEmpty();
        _backfillStore.SavedState.Should().BeNull("nothing is recorded, so Inbox reads stay on the queue row");
    }

    [Fact]
    public async Task RunAsync_ShouldCommitEachBatchWithItsProgressMarkerAndReleaseTheTracker()
    {
        for (var index = 0; index < 4; index++)
        {
            SeedLegacyRow(text: $"row {index}");
        }

        await CreateService().RunAsync(batchSize: 2);

        // Two batches plus the final completion write.
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
        _backfillStore.TrackerReleases.Should().Be(2, "the tracker is released once per committed batch");
        _backfillStore.SavedState!.MigratedCount.Should().Be(4);
        _backfillStore.SavedState.IsComplete.Should().BeTrue();
    }

    private static void EditQueuePayloadOnly(
        LlmRequest request,
        string text,
        string? titleHint = null,
        string? externalRef = null)
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            text,
            TitleHint: titleHint,
            ExternalRef: externalRef);
        request.UpdatePayload(CaptureRequestContract.SerializePayload(payload));
    }

    /// <summary>An in-memory <see cref="ICaptureStore"/> that keeps the aggregates it is given.</summary>
    private sealed class FakeCaptureStore : ICaptureStore
    {
        private readonly Dictionary<Guid, Capture> _captures = new();

        public IReadOnlyList<Capture> All => _captures.Values.ToList();

        public void Forget(Guid id) => _captures.Remove(id);

        public Task AddAsync(Capture capture, CancellationToken cancellationToken = default)
        {
            _captures[capture.Id] = capture;
            return Task.CompletedTask;
        }

        public Task<Capture?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_captures.TryGetValue(id, out var capture) && capture.UserId == userId ? capture : null);

        public Task<Capture?> GetByIdForUpdateAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
            => GetByIdForUserAsync(id, userId, cancellationToken);

        public Task UpdateAsync(Capture capture, CancellationToken cancellationToken = default)
        {
            _captures[capture.Id] = capture;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Capture>> GetByIdsForUserAsync(
            IReadOnlyCollection<Guid> ids,
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Capture>>(
                ids.Where(_captures.ContainsKey)
                    .Select(id => _captures[id])
                    .Where(capture => capture.UserId == userId)
                    .ToList());

        public Task<IReadOnlyList<CaptureListMaterial>> GetListMaterialForUserAsync(
            IReadOnlyCollection<Guid> ids,
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CaptureListMaterial>>(
                ids.Where(_captures.ContainsKey)
                    .Select(id => _captures[id])
                    .Where(capture => capture.UserId == userId)
                    .Select(capture => new CaptureListMaterial(
                        capture.Id,
                        capture.LegacySourceSnapshot,
                        capture.CapturedAtServer,
                        capture.UpdatedAt,
                        capture.CurrentText))
                    .ToList());

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_captures.ContainsKey(id));

        public Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_captures.Values.Count(capture => capture.UserId == userId));

        public Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var removed = _captures.Values.Where(capture => capture.UserId == userId).Select(c => c.Id).ToList();
            foreach (var id in removed)
            {
                _captures.Remove(id);
            }

            return Task.FromResult(removed.Count);
        }
    }

    /// <summary>
    /// An in-memory <see cref="ICaptureBackfillStore"/> whose backlog is the same DIVERGENCE join the
    /// EF implementation runs: a row leaves it only once a capture exists AND that capture is at
    /// least as fresh as the queue row.
    /// </summary>
    private sealed class FakeBackfillStore : ICaptureBackfillStore
    {
        private readonly List<LlmRequest> _rows = new();
        private readonly FakeCaptureStore _captures;

        public FakeBackfillStore(FakeCaptureStore captures)
        {
            _captures = captures;
        }

        public CaptureBackfillState? SavedState { get; private set; }

        public int TrackerReleases { get; private set; }

        public void Add(LlmRequest request) => _rows.Add(request);

        /// <summary>Discards everything after the first <paramref name="committed"/> captures, as a crash would.</summary>
        public void SimulateCrashAfter(int committed)
        {
            foreach (var capture in _captures.All.Skip(committed))
            {
                _captures.Forget(capture.Id);
            }
        }

        private IEnumerable<LlmRequest> Backlog =>
            _rows.Where(row =>
            {
                var capture = _captures.All.FirstOrDefault(c => c.Id == row.Id);
                return capture is null || capture.UpdatedAt < row.UpdatedAt;
            });

        public Task<IReadOnlyList<LlmRequest>> GetLegacyCaptureBacklogAsync(
            int batchSize,
            IReadOnlyCollection<Guid> excludedIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LlmRequest>>(
                Backlog.Where(row => !excludedIds.Contains(row.Id))
                    .OrderBy(row => row.CreatedAt)
                    .ThenBy(row => row.Id)
                    .Take(batchSize)
                    .ToList());

        public Task<int> CountLegacyCaptureBacklogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Backlog.Count());

        public Task ReleaseTrackedBatchAsync(CancellationToken cancellationToken = default)
        {
            TrackerReleases++;
            return Task.CompletedTask;
        }

        public Task<CaptureBackfillState?> GetStateAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(SavedState);

        public Task SaveStateAsync(CaptureBackfillState state, CancellationToken cancellationToken = default)
        {
            SavedState = state;
            return Task.CompletedTask;
        }
    }
}
