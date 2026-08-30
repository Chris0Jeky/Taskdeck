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
/// CF-01 (#2255), late review findings on #2320: the canonical intake stores a payload external
/// reference as its own immutable asset, and a post-intake edit produces a superseding asset rather
/// than rewriting the one already stored.
/// </summary>
public sealed class CaptureIntakeSourceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly Mock<ILlmQueueRepository> _queue = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAutomationProposalRepository> _proposals = new();
    private readonly Mock<ICaptureStore> _captureStore = new();
    private readonly Guid _userId = Guid.NewGuid();

    public CaptureIntakeSourceTests()
    {
        _unitOfWork.SetupGet(unit => unit.LlmQueue).Returns(_queue.Object);
        _unitOfWork.SetupGet(unit => unit.Users).Returns(_users.Object);
        _unitOfWork.SetupGet(unit => unit.AutomationProposals).Returns(_proposals.Object);
        _users
            .Setup(repository => repository.GetByIdAsync(_userId, default))
            .ReturnsAsync(new User("capture-user", "capture-user@example.com", "hash"));
        _queue
            .Setup(repository => repository.AddAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmRequest request, CancellationToken _) => request);
    }

    private CaptureService CreateService() =>
        new(
            _unitOfWork.Object,
            _authorization.Object,
            _captureStore.Object,
            new ContextFabricSettings { DualWriteCaptures = true },
            backfillStore: null,
            logger: null);

    [Fact]
    public async Task CreateAsync_ShouldStoreAPayloadExternalReferenceAsItsOwnAsset()
    {
        Capture? admitted = null;
        _captureStore
            .Setup(store => store.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => admitted = capture)
            .Returns(Task.CompletedTask);

        var result = await CreateService().CreateAsync(
            _userId,
            new CreateCaptureItemDto(null, "read this later", "webClip", ExternalRef: "https://example.test/article"));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        admitted!.SourceAssets.Should().HaveCount(2);
        admitted.SourceAssets[0].StorageKind.Should().Be(SourceAssetStorageKind.InlineText);
        admitted.SourceAssets[1].StorageKind.Should().Be(SourceAssetStorageKind.ExternalReference);
        admitted.SourceAssets[1].ExternalReference.Should().Be("https://example.test/article");
        admitted.SourceAssets[1].MediaType.Should().Be(SourceAsset.UriListMediaType);
        admitted.PrimaryModality.Should().Be(CaptureModality.Text, "the first asset still decides the summary");
    }

    [Fact]
    public async Task CreateAsync_WithoutAnExternalReference_ShouldStoreOnlyTheTextAsset()
    {
        Capture? admitted = null;
        _captureStore
            .Setup(store => store.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => admitted = capture)
            .Returns(Task.CompletedTask);

        await CreateService().CreateAsync(_userId, new CreateCaptureItemDto(null, "typed note", "typed"));

        admitted!.SourceAssets.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateSuggestionAsync_ShouldSupersedeTheSourceRatherThanRewriteIt()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "call Dana");
        var request = new LlmRequest(
            _userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(payload),
            boardId: null);
        var durable = Capture.FromQueueRequest(
            request.Id,
            _userId,
            CaptureSource.Typed,
            contextBoardId: null,
            capturedAtClient: null,
            userTitle: null,
            capturedAtServer: request.CreatedAt,
            sourceText: "call Dana");
        _queue.Setup(repository => repository.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(request.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);

        var result = await CreateService().UpdateSuggestionAsync(
            _userId,
            request.Id,
            new UpdateCaptureSuggestionDto("call Dana on Friday"));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        durable.SourceAssets.Should().HaveCount(2, "the original source is kept, not rewritten");
        durable.SourceAssets[0].TextPayload!.Text.Should().Be("call Dana");
        durable.SourceAssets[0].IsActive.Should().BeFalse();
        durable.SourceAssets[1].TextPayload!.Text.Should().Be("call Dana on Friday");
        durable.SourceAssets[1].SupersedesAssetId.Should().Be(durable.SourceAssets[0].Id);
        durable.CurrentText.Should().Be("call Dana on Friday");
        result.Value!.RawText.Should().Be("call Dana on Friday");
        _captureStore.Verify(
            store => store.UpdateAsync(durable, It.IsAny<CancellationToken>()),
            Times.Once,
            "the aggregate mutation commits through the unit of work");
    }

    [Fact]
    public async Task UpdateSuggestionAsync_WithNoDurableCapture_ShouldStillSucceed()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "call Dana");
        var request = new LlmRequest(
            _userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(payload),
            boardId: null);
        _queue.Setup(repository => repository.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(request.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Capture?)null);

        var result = await CreateService().UpdateSuggestionAsync(
            _userId,
            request.Id,
            new UpdateCaptureSuggestionDto("call Dana on Friday"));

        result.IsSuccess.Should().BeTrue("the durable side is never the reason a shipped edit fails");
        result.Value!.RawText.Should().Be("call Dana on Friday");
    }

    [Fact]
    public async Task CancelAsync_ShouldArchiveTheDurableCaptureWithoutErasingItsOutcomes()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "call Dana");
        var request = new LlmRequest(
            _userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(payload),
            boardId: null);
        var durable = Capture.FromQueueRequest(
            request.Id,
            _userId,
            CaptureSource.Typed,
            contextBoardId: null,
            capturedAtClient: null,
            userTitle: null,
            capturedAtServer: request.CreatedAt,
            sourceText: "call Dana",
            processingSummary: CaptureProcessingSummary.Ready,
            actionState: CaptureActionState.Acted);
        _queue.Setup(repository => repository.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _captureStore
            .Setup(store => store.GetByIdForUpdateAsync(request.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(durable);

        var result = await CreateService().CancelAsync(_userId, request.Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        durable.Disposition.Should().Be(CaptureUserDisposition.Archived);
        durable.ProcessingSummary.Should().Be(CaptureProcessingSummary.Ready);
        durable.ActionState.Should().Be(CaptureActionState.Acted);
        durable.Timeline.Should().Be(CaptureTimelineStep.Archived);
    }
}
