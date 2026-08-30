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
/// CF-01 (#2255): the ID-preserving backfill of legacy capture queue rows. It must be idempotent,
/// resumable, derive the three state axes from what each row recorded, store the material as
/// immutable source assets, and never let one unmappable row abort the run or wedge the read switch.
/// </summary>
public sealed class CaptureBackfillServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICaptureStore> _captureStore = new();
    private readonly FakeBackfillStore _backfillStore = new();
    private readonly List<Capture> _written = new();
    private readonly Guid _userId = Guid.NewGuid();

    public CaptureBackfillServiceTests()
    {
        _captureStore
            .Setup(store => store.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) =>
            {
                _written.Add(capture);
                _backfillStore.MarkMigrated(capture.Id);
            })
            .Returns(Task.CompletedTask);
    }

    private CaptureBackfillService CreateService(bool backfill = true) =>
        new(
            _unitOfWork.Object,
            _captureStore.Object,
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
        var request = new LlmRequest(
            _userId,
            CaptureRequestContract.ResolveRequestTypeForSource(source),
            CaptureRequestContract.SerializePayload(payload),
            boardId);

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

    [Fact]
    public async Task RunAsync_ShouldMigrateEveryLegacyRowUnderItsOwnId()
    {
        var first = SeedLegacyRow(text: "first");
        var second = SeedLegacyRow(text: "second");

        var result = await CreateService().RunAsync();

        result.Migrated.Should().Be(2);
        result.Remaining.Should().Be(0);
        result.Complete.Should().BeTrue();
        _written.Select(capture => capture.Id).Should().BeEquivalentTo(new[] { first.Id, second.Id });
        _written.Should().OnlyContain(capture => capture.LegacyRequestId == capture.Id);
        _written.Should().OnlyContain(capture => capture.UserId == _userId);
    }

    [Fact]
    public async Task RunAsync_ShouldBeIdempotent()
    {
        SeedLegacyRow();
        var service = CreateService();

        var first = await service.RunAsync();
        var second = await service.RunAsync();

        first.Migrated.Should().Be(1);
        second.Migrated.Should().Be(0, "a migrated row leaves the backlog forever");
        _written.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ShouldResumeAcrossBatchesAndAcrossRuns()
    {
        for (var index = 0; index < 5; index++)
        {
            SeedLegacyRow(text: $"row {index}");
        }

        // A crash after the first committed batch: the second run picks up exactly what is left.
        var interrupted = await CreateService().RunAsync(batchSize: 2);
        _backfillStore.SimulateCrashAfter(interrupted.Migrated);

        var resumed = await CreateService().RunAsync(batchSize: 2);

        (interrupted.Migrated + resumed.Migrated).Should().Be(5);
        _written.Select(capture => capture.Id).Distinct().Should().HaveCount(5, "nothing is created twice");
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

        var capture = _written.Should().ContainSingle().Subject;
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

        var capture = _written.Should().ContainSingle().Subject;
        capture.ProcessingSummary.Should().Be(expectedProcessing);
        capture.ActionState.Should().Be(expectedAction);
        capture.Timeline.Should().Be(expectedTimeline);
    }

    [Fact]
    public async Task RunAsync_ShouldCarryAKeptDispositionAndItsRememberIntent()
    {
        SeedLegacyRow(disposition: new CaptureDispositionV1(CaptureDisposition.Kept, DateTimeOffset.UtcNow, _userId));

        await CreateService().RunAsync();

        var capture = _written.Should().ContainSingle().Subject;
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

        var capture = _written.Should().ContainSingle().Subject;
        capture.Disposition.Should().Be(CaptureUserDisposition.Archived);
        capture.ActionState.Should().Be(CaptureActionState.Acted, "archiving does not erase what was applied");
        capture.ProcessingSummary.Should().Be(CaptureProcessingSummary.Ready);
        capture.Timeline.Should().Be(CaptureTimelineStep.Archived);
    }

    [Fact]
    public async Task RunAsync_ShouldSkipAnUnmappableRowWithoutAbortingOrWedgingTheReadSwitch()
    {
        SeedLegacyRow(text: "healthy row");
        var poisoned = SeedLegacyRow(text: "poisoned row");
        _backfillStore.Poison(poisoned.Id);
        _captureStore
            .Setup(store => store.AddAsync(
                It.Is<Capture>(capture => capture.Id == poisoned.Id),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Domain.Exceptions.DomainException(
                Domain.Exceptions.ErrorCodes.ValidationError,
                "unmappable"));

        var result = await CreateService().RunAsync(batchSize: 1);

        result.Migrated.Should().Be(1);
        result.Skipped.Should().BeGreaterThan(0);
        result.Complete.Should().BeTrue("one bad row must not hold the read switch hostage");
        result.Remaining.Should().Be(1, "the skipped row stays readable through its queue row");
    }

    [Fact]
    public async Task RunAsync_WithTheFlagOff_ShouldDoNothingAndLeaveTheMarkerIncomplete()
    {
        SeedLegacyRow();

        var result = await CreateService(backfill: false).RunAsync();

        result.Ran.Should().BeFalse();
        result.Complete.Should().BeFalse();
        _written.Should().BeEmpty();
        _backfillStore.SavedState.Should().BeNull("nothing is recorded, so Inbox reads stay on the queue row");
    }

    [Fact]
    public async Task RunAsync_ShouldCommitEachBatchWithItsProgressMarker()
    {
        for (var index = 0; index < 4; index++)
        {
            SeedLegacyRow(text: $"row {index}");
        }

        await CreateService().RunAsync(batchSize: 2);

        // Two batches plus the final completion write.
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
        _backfillStore.SavedState!.MigratedCount.Should().Be(4);
        _backfillStore.SavedState.IsComplete.Should().BeTrue();
    }

    /// <summary>
    /// An in-memory <see cref="ICaptureBackfillStore"/> whose backlog is the same anti-join the EF
    /// implementation runs: a row leaves it as soon as its capture is written.
    /// </summary>
    private sealed class FakeBackfillStore : ICaptureBackfillStore
    {
        private readonly List<LlmRequest> _rows = new();
        private readonly HashSet<Guid> _migrated = new();
        private readonly HashSet<Guid> _poisoned = new();

        public CaptureBackfillState? SavedState { get; private set; }

        public void Add(LlmRequest request) => _rows.Add(request);

        public void MarkMigrated(Guid id)
        {
            if (!_poisoned.Contains(id))
            {
                _migrated.Add(id);
            }
        }

        /// <summary>Marks a row that the capture store will refuse, so it never leaves the backlog.</summary>
        public void Poison(Guid id) => _poisoned.Add(id);

        /// <summary>Drops everything after the first <paramref name="committed"/> rows, as a crash would.</summary>
        public void SimulateCrashAfter(int committed)
        {
            var keep = _migrated.Take(committed).ToHashSet();
            _migrated.Clear();
            foreach (var id in keep)
            {
                _migrated.Add(id);
            }
        }

        public Task<IReadOnlyList<LlmRequest>> GetLegacyCaptureBacklogAsync(
            int batchSize,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LlmRequest>>(
                _rows.Where(row => !_migrated.Contains(row.Id))
                    .OrderBy(row => row.CreatedAt)
                    .ThenBy(row => row.Id)
                    .Take(batchSize)
                    .ToList());

        public Task<int> CountLegacyCaptureBacklogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_rows.Count(row => !_migrated.Contains(row.Id)));

        public Task<CaptureBackfillState?> GetStateAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(SavedState);

        public Task SaveStateAsync(CaptureBackfillState state, CancellationToken cancellationToken = default)
        {
            SavedState = state;
            return Task.CompletedTask;
        }
    }
}
