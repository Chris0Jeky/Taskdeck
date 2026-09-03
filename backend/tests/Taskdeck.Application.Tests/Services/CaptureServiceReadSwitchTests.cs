using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;
using Capture = Taskdeck.Domain.Entities.Capture;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// CF-01 (#2255) read switch: once the ID-preserving backfill has completed, Inbox list and get
/// resolve a capture material - its immutable source text, its capture source and its intake time -
/// through <see cref="ICaptureStore"/> instead of parsing the queue row payload JSON.
/// <para>
/// The switch is guarded twice: globally on the backfill marker, and per item on whether that
/// capture actually has a durable row. A capture can never disappear from the Inbox because of it,
/// and the DTOs are byte-identical either way while the aggregate agrees with the payload.
/// </para>
/// </summary>
public sealed class CaptureServiceReadSwitchTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly Mock<ILlmQueueRepository> _queue = new();
    private readonly Mock<IAutomationProposalRepository> _proposals = new();
    private readonly Mock<ICaptureStore> _captureStore = new();
    private readonly Mock<ICaptureBackfillStore> _backfillStore = new();
    private readonly Guid _userId = Guid.NewGuid();

    public CaptureServiceReadSwitchTests()
    {
        _unitOfWork.SetupGet(unit => unit.LlmQueue).Returns(_queue.Object);
        _unitOfWork.SetupGet(unit => unit.AutomationProposals).Returns(_proposals.Object);
        _proposals
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AutomationProposal>());
    }

    private CaptureService CreateService(bool backfillComplete = true, bool readFromStore = true)
    {
        var state = CaptureBackfillState.ForLegacyQueue(DateTimeOffset.UtcNow.AddMinutes(-1));
        if (backfillComplete)
        {
            state.MarkComplete(DateTimeOffset.UtcNow);
        }

        _backfillStore
            .Setup(store => store.GetStateAsync(
                CaptureBackfillState.LegacyQueueBackfillKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        return new CaptureService(
            _unitOfWork.Object,
            _authorization.Object,
            _captureStore.Object,
            new ContextFabricSettings { DualWriteCaptures = true, ReadCapturesFromStore = readFromStore },
            _backfillStore.Object,
            logger: null);
    }

    private LlmRequest QueueRow(string text, CaptureSource source = CaptureSource.Typed)
    {
        var payload = new CapturePayloadV1(CaptureRequestContract.CurrentSchemaVersion, source, text);
        return new LlmRequest(
            _userId,
            CaptureRequestContract.ResolveRequestTypeForSource(source),
            CaptureRequestContract.SerializePayload(payload),
            boardId: null);
    }

    private Capture DurableFor(LlmRequest request, string text, CaptureSource source = CaptureSource.Typed) =>
        Capture.FromQueueRequest(
            request.Id,
            _userId,
            source,
            contextBoardId: null,
            capturedAtClient: null,
            userTitle: null,
            capturedAtServer: request.CreatedAt,
            sourceText: text);

    private CaptureListMaterial MaterialFor(
        LlmRequest request,
        string text,
        CaptureSource source = CaptureSource.Typed,
        DateTimeOffset? updatedAt = null) =>
        new(request.Id, source, request.CreatedAt, updatedAt ?? DateTimeOffset.UtcNow.AddMinutes(5), text);

    private void SetupListMaterial(params CaptureListMaterial[] material) =>
        _captureStore
            .Setup(store => store.GetListMaterialForUserAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(material);

    private void SetupList(params LlmRequest[] rows)
    {
        _queue
            .Setup(repository => repository.GetCapturesByUserAsync(
                _userId, It.IsAny<int>(), 0, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _queue
            .Setup(repository => repository.GetCapturesByUserAsync(
                _userId, It.IsAny<int>(), It.Is<int>(offset => offset > 0), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LlmRequest>());
    }

    [Fact]
    public async Task ListAsync_ShouldReadTheCaptureMaterialThroughTheStore()
    {
        var row = QueueRow("payload text");
        SetupList(row);
        // The aggregate is authoritative for the capture own material; the payload text below is
        // what the shipped path would have shown.
        SetupListMaterial(MaterialFor(row, "durable source text", CaptureSource.Paste));

        var result = await CreateService().ListAsync(_userId, new CaptureListFilterDto());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var summary = result.Value.Should().ContainSingle().Subject;
        summary.Id.Should().Be(row.Id);
        summary.TextExcerpt.Should().Be("durable source text");
        summary.Source.Should().Be(CaptureSource.Paste, "the source snapshot lives on the aggregate now");
        summary.CreatedAt.Should().Be(row.CreatedAt);
        summary.Status.Should().Be(CaptureStatus.New, "queue status is still job state");
        _captureStore.Verify(
            store => store.GetListMaterialForUserAsync(It.IsAny<IReadOnlyCollection<Guid>>(), _userId, It.IsAny<CancellationToken>()),
            Times.Once,
            "one owner-scoped batch per page, never one query per item");
        _captureStore.Verify(
            store => store.GetByIdsForUserAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a listing must never load whole aggregates, superseded revisions included");
    }

    [Fact]
    public async Task ListAsync_ShouldKeepAnItemWithNoDurableRowVisible()
    {
        var mirrored = QueueRow("mirrored text");
        var notBackfilled = QueueRow("only on the queue row");
        SetupList(mirrored, notBackfilled);
        SetupListMaterial(MaterialFor(mirrored, "mirrored text"));

        var result = await CreateService().ListAsync(_userId, new CaptureListFilterDto());

        result.Value.Should().HaveCount(2, "the read switch never removes an Inbox item");
        result.Value.Single(item => item.Id == notBackfilled.Id).TextExcerpt
            .Should().Be("only on the queue row", "a gap falls back to the queue payload");
    }

    [Fact]
    public async Task ListAsync_WithAnIncompleteBackfill_ShouldNotTouchTheStore()
    {
        var row = QueueRow("payload text");
        SetupList(row);

        var result = await CreateService(backfillComplete: false).ListAsync(_userId, new CaptureListFilterDto());

        result.Value.Should().ContainSingle().Which.TextExcerpt.Should().Be("payload text");
        _captureStore.Verify(
            store => store.GetListMaterialForUserAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a host whose backfill has not run keeps reading the queue row");
    }

    [Fact]
    public async Task ListAsync_WithTheReadFlagOff_ShouldNotTouchTheStore()
    {
        var row = QueueRow("payload text");
        SetupList(row);

        var result = await CreateService(readFromStore: false).ListAsync(_userId, new CaptureListFilterDto());

        result.Value.Should().ContainSingle().Which.TextExcerpt.Should().Be("payload text");
        _captureStore.Verify(
            store => store.GetListMaterialForUserAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListAsync_ShouldProduceIdenticalDtosWhenTheAggregateAgreesWithThePayload()
    {
        var row = QueueRow("identical text", CaptureSource.TranscriptPaste);
        SetupList(row);
        SetupListMaterial(MaterialFor(row, "identical text", CaptureSource.TranscriptPaste));

        var throughStore = await CreateService().ListAsync(_userId, new CaptureListFilterDto());
        var throughQueue = await CreateService(backfillComplete: false).ListAsync(_userId, new CaptureListFilterDto());

        throughStore.Value.Should().BeEquivalentTo(
            throughQueue.Value,
            "the Inbox is byte-identical across the read switch");
    }

    // ------------------------------------------------------------- divergence guard
    [Fact]
    public async Task ListAsync_ShouldServeTheQueueRowWhenTheAggregateHasFallenBehindIt()
    {
        // Round-1 review, HIGH-1 and MEDIUM-2: an edit that reached the queue row but not the
        // aggregate (dual-write off, or a durable write that failed and was swallowed) must not be
        // hidden behind stale aggregate text. The queue row is the newer writer, so it wins.
        var row = QueueRow("corrected draft");
        SetupList(row);
        SetupListMaterial(MaterialFor(
            row,
            "first draft",
            updatedAt: row.UpdatedAt.AddMinutes(-5)));

        var result = await CreateService().ListAsync(_userId, new CaptureListFilterDto());

        result.Value.Should().ContainSingle().Which.TextExcerpt
            .Should().Be("corrected draft", "the read path prefers whichever writer moved last");
    }

    [Fact]
    public async Task ListAsync_ShouldKeepUsingTheAggregateWhenItAgreesWithAQueueRowThatMovedOn()
    {
        // A triage transition stamps the queue row without touching the capture. The texts still
        // agree, so this is not divergence and the read switch must stay on.
        var row = QueueRow("same text");
        SetupList(row);
        SetupListMaterial(MaterialFor(
            row,
            "same text",
            CaptureSource.Voice,
            updatedAt: row.UpdatedAt.AddMinutes(-5)));

        var result = await CreateService().ListAsync(_userId, new CaptureListFilterDto());

        result.Value.Should().ContainSingle().Which.Source
            .Should().Be(CaptureSource.Voice, "agreement on text means the aggregate is still authoritative");
    }

    // ------------------------------------------------------------- mutation responses obey the gate
    [Fact]
    public async Task KeepAsync_WithTheReadFlagOff_ShouldAnswerFromTheQueueRowNotTheAggregate()
    {
        // Round-1 review, MEDIUM-1: a mutation response is a read too. With the read switch
        // disarmed, a keep must not hand back aggregate text that a GET would refuse to serve.
        var row = QueueRow("payload text");
        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _queue.Setup(repository => repository.TrySetCaptureDispositionAsync(
                row.Id,
                It.IsAny<RequestStatus>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<RequestStatus>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _proposals
            .Setup(repository => repository.GetBySourceReferenceAsync(
                It.IsAny<ProposalSourceType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DurableFor(row, "aggregate text"));

        var result = await CreateService(readFromStore: false).KeepAsync(_userId, row.Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.RawText.Should().Be("payload text");
    }

    [Fact]
    public async Task KeepAsync_ShouldStillRecordTheDurableDispositionWhenTheReadFlagIsOff()
    {
        // The gate governs what a response SHOWS, never whether the aggregate is kept in step: an
        // aggregate allowed to drift is exactly what the divergence repair exists to prevent.
        var row = QueueRow("payload text");
        var durable = DurableFor(row, "payload text");
        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _queue.Setup(repository => repository.TrySetCaptureDispositionAsync(
                row.Id,
                It.IsAny<RequestStatus>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<RequestStatus>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _proposals
            .Setup(repository => repository.GetBySourceReferenceAsync(
                It.IsAny<ProposalSourceType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);

        await CreateService(readFromStore: false).KeepAsync(_userId, row.Id);

        durable.Disposition.Should().Be(CaptureUserDisposition.Kept);
        _unitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CaptureDisposition.Kept)]
    [InlineData(CaptureDisposition.Archived)]
    public async Task SetDispositionAsync_ShouldReconcileQueueTextBeforeStampingTheDurableCapture(
        CaptureDisposition disposition)
    {
        // CF-01c (#2347): the queue row can be newer after an out-of-band edit. Keep/Archive both
        // Touch the aggregate, so applying the disposition first would mask the stale durable text
        // from both the read guard and the reconcile backlog forever.
        var row = QueueRow("first draft");
        var durable = DurableFor(row, "first draft");
        var originalAsset = durable.SourceAssets.Should().ContainSingle().Subject;
        var correctedPayload = CaptureRequestContract.ParseStoredPayload(row.Payload) with
        {
            Text = "corrected draft"
        };
        row.UpdatePayload(CaptureRequestContract.SerializePayload(correctedPayload));

        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _queue.Setup(repository => repository.TrySetCaptureDispositionAsync(
                row.Id,
                It.IsAny<RequestStatus>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<RequestStatus>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _proposals
            .Setup(repository => repository.GetBySourceReferenceAsync(
                It.IsAny<ProposalSourceType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);
        _captureStore
            .Setup(store => store.GetByIdForUserAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);

        var service = CreateService();
        var mutation = disposition == CaptureDisposition.Kept
            ? await service.KeepAsync(_userId, row.Id)
            : await service.ArchiveAsync(_userId, row.Id);
        var reread = await service.GetByIdAsync(_userId, row.Id);

        mutation.IsSuccess.Should().BeTrue(mutation.ErrorMessage);
        mutation.Value!.RawText.Should().Be("corrected draft");
        reread.IsSuccess.Should().BeTrue(reread.ErrorMessage);
        reread.Value!.RawText.Should().Be("corrected draft");
        durable.CurrentText.Should().Be("corrected draft");
        durable.Disposition.Should().Be(CaptureUserDispositionMapping.FromLegacy(disposition));
        durable.SourceAssets.Should().HaveCount(2, "the correction supersedes rather than rewriting lineage");
        originalAsset.IsActive.Should().BeFalse();
        durable.SourceAssets[1].SupersedesAssetId.Should().Be(originalAsset.Id);
    }

    [Fact]
    public async Task ArchiveAsync_WhenDurableTextCannotBeRepaired_ShouldKeepTheQueueFallbackAndStampUnchanged()
    {
        var row = QueueRow("first draft");
        var durable = DurableFor(row, "first draft");
        durable.Archive();
        var archivedStamp = durable.UpdatedAt;
        var correctedPayload = CaptureRequestContract.ParseStoredPayload(row.Payload) with
        {
            Text = "corrected draft"
        };
        row.UpdatePayload(CaptureRequestContract.SerializePayload(correctedPayload));

        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _queue.Setup(repository => repository.TrySetCaptureDispositionAsync(
                row.Id,
                It.IsAny<RequestStatus>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<RequestStatus>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _proposals
            .Setup(repository => repository.GetBySourceReferenceAsync(
                It.IsAny<ProposalSourceType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);
        _captureStore
            .Setup(store => store.GetByIdForUserAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);

        var service = CreateService();
        var mutation = await service.ArchiveAsync(_userId, row.Id);
        var reread = await service.GetByIdAsync(_userId, row.Id);

        mutation.IsSuccess.Should().BeTrue(mutation.ErrorMessage);
        mutation.Value!.RawText.Should().Be("corrected draft", "the accepted queue write remains readable");
        reread.Value!.RawText.Should().Be("corrected draft");
        durable.CurrentText.Should().Be("first draft");
        durable.UpdatedAt.Should().Be(archivedStamp, "a failed repair must not fabricate freshness");
        _captureStore.Verify(
            store => store.UpdateAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task KeepAsync_IdempotentRetryThatLosesTheQueueCas_ShouldNotOverwriteNewerDurableText()
    {
        var row = QueueRow("stale retry text");
        var keptPayload = CaptureRequestContract.ParseStoredPayload(row.Payload) with
        {
            Disposition = new CaptureDispositionV1(
                CaptureDisposition.Kept,
                DateTimeOffset.UtcNow,
                _userId)
        };
        row.UpdatePayload(CaptureRequestContract.SerializePayload(keptPayload));
        var durable = DurableFor(row, "stale retry text");
        durable.Keep();
        durable.SupersedeInlineTextSource("newer correction");
        var newerStamp = durable.UpdatedAt;

        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _queue.Setup(repository => repository.TrySetCaptureDispositionAsync(
                row.Id,
                It.IsAny<RequestStatus>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<RequestStatus>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);

        var result = await CreateService().KeepAsync(_userId, row.Id);

        result.IsSuccess.Should().BeFalse("a stale retry must lose before touching the aggregate");
        result.ErrorCode.Should().Be(Taskdeck.Domain.Exceptions.ErrorCodes.Conflict);
        durable.CurrentText.Should().Be("newer correction");
        durable.UpdatedAt.Should().Be(newerStamp);
        _captureStore.Verify(
            store => store.UpdateAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelAsync_WhenTheQueueCasLoses_ShouldNotOverwriteOrArchiveNewerDurableText()
    {
        var row = QueueRow("stale cancel text");
        var durable = DurableFor(row, "stale cancel text");
        durable.SupersedeInlineTextSource("newer correction");
        var newerStamp = durable.UpdatedAt;

        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _queue.Setup(repository => repository.TrySetCaptureDispositionAsync(
                row.Id,
                It.IsAny<RequestStatus>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<RequestStatus>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);

        var result = await CreateService().CancelAsync(_userId, row.Id);

        result.IsSuccess.Should().BeFalse("a stale cancellation must lose before touching the aggregate");
        result.ErrorCode.Should().Be(Taskdeck.Domain.Exceptions.ErrorCodes.Conflict);
        durable.CurrentText.Should().Be("newer correction");
        durable.Disposition.Should().Be(CaptureUserDisposition.Active);
        durable.UpdatedAt.Should().Be(newerStamp);
        _captureStore.Verify(
            store => store.UpdateAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReadTheTextAndSourceFromTheAggregate()
    {
        var row = QueueRow("payload text");
        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _captureStore
            .Setup(store => store.GetByIdForUserAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DurableFor(row, "durable source text", CaptureSource.Voice));

        var result = await CreateService().GetByIdAsync(_userId, row.Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.RawText.Should().Be("durable source text");
        result.Value.Source.Should().Be(CaptureSource.Voice);
        result.Value.CreatedAt.Should().Be(row.CreatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldStayOwnerScopedThroughTheStore()
    {
        var row = QueueRow("payload text");
        var otherUser = Guid.NewGuid();
        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        var result = await CreateService().GetByIdAsync(otherUser, row.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(Taskdeck.Domain.Exceptions.ErrorCodes.Forbidden, "cross-user reads keep the shipped contract");
        _captureStore.Verify(
            store => store.GetByIdForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WithNoDurableRow_ShouldFallBackToTheQueuePayload()
    {
        var row = QueueRow("payload text");
        _queue.Setup(repository => repository.GetByIdAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _captureStore
            .Setup(store => store.GetByIdForUserAsync(row.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Capture?)null);

        var result = await CreateService().GetByIdAsync(_userId, row.Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.RawText.Should().Be("payload text");
    }
}
