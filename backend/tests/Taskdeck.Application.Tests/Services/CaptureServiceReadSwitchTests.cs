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
        _captureStore
            .Setup(store => store.GetByIdsForUserAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { DurableFor(row, "durable source text", CaptureSource.Paste) });

        var result = await CreateService().ListAsync(_userId, new CaptureListFilterDto());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var summary = result.Value.Should().ContainSingle().Subject;
        summary.Id.Should().Be(row.Id);
        summary.TextExcerpt.Should().Be("durable source text");
        summary.Source.Should().Be(CaptureSource.Paste, "the source snapshot lives on the aggregate now");
        summary.CreatedAt.Should().Be(row.CreatedAt);
        summary.Status.Should().Be(CaptureStatus.New, "queue status is still job state");
        _captureStore.Verify(
            store => store.GetByIdsForUserAsync(It.IsAny<IReadOnlyCollection<Guid>>(), _userId, It.IsAny<CancellationToken>()),
            Times.Once,
            "one owner-scoped batch per page, never one query per item");
    }

    [Fact]
    public async Task ListAsync_ShouldKeepAnItemWithNoDurableRowVisible()
    {
        var mirrored = QueueRow("mirrored text");
        var notBackfilled = QueueRow("only on the queue row");
        SetupList(mirrored, notBackfilled);
        _captureStore
            .Setup(store => store.GetByIdsForUserAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { DurableFor(mirrored, "mirrored text") });

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
            store => store.GetByIdsForUserAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
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
            store => store.GetByIdsForUserAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListAsync_ShouldProduceIdenticalDtosWhenTheAggregateAgreesWithThePayload()
    {
        var row = QueueRow("identical text", CaptureSource.TranscriptPaste);
        SetupList(row);
        _captureStore
            .Setup(store => store.GetByIdsForUserAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { DurableFor(row, "identical text", CaptureSource.TranscriptPaste) });

        var throughStore = await CreateService().ListAsync(_userId, new CaptureListFilterDto());
        var throughQueue = await CreateService(backfillComplete: false).ListAsync(_userId, new CaptureListFilterDto());

        throughStore.Value.Should().BeEquivalentTo(
            throughQueue.Value,
            "the Inbox is byte-identical across the read switch");
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
