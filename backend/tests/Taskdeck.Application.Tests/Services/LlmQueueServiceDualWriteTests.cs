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
/// The external audit of PR #2280 found that <c>POST /api/llm-queue</c> accepted capture-shaped
/// request types and persisted only the queue row, bypassing the dual-write seam. Every creation
/// path now goes through the canonical <see cref="CaptureIntakeService"/>; these tests pin the
/// enqueue path to the same behaviour as <see cref="CaptureService.CreateAsync"/>.
/// </summary>
public sealed class LlmQueueServiceDualWriteTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();
    private readonly Mock<ILlmQueueRepository> _llmQueueRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ICaptureStore> _captureStoreMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    public LlmQueueServiceDualWriteTests()
    {
        _unitOfWorkMock.SetupGet(u => u.LlmQueue).Returns(_llmQueueRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Users).Returns(_userRepositoryMock.Object);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId, default))
            .ReturnsAsync(new User("queue-user", "queue-user@example.com", "hash"));
        _llmQueueRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmRequest request, CancellationToken _) => request);
    }

    private LlmQueueService CreateService(bool dualWrite) =>
        new(
            _unitOfWorkMock.Object,
            _authorizationServiceMock.Object,
            sandboxSettings: null,
            _captureStoreMock.Object,
            new ContextFabricSettings { DualWriteCaptures = dualWrite });

    private static string CapturePayload(CaptureSource source, string text, CaptureDispositionV1? disposition = null) =>
        CaptureRequestContract.SerializePayload(
            new CapturePayloadV1(CaptureRequestContract.CurrentSchemaVersion, source, text, TitleHint: "Enqueued", Disposition: disposition));

    [Fact]
    public async Task AddToQueueAsync_CaptureRequest_WithDualWriteEnabled_ShouldMirrorLikeCaptureService()
    {
        Capture? mirrored = null;
        _captureStoreMock
            .Setup(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => mirrored = capture)
            .Returns(Task.CompletedTask);
        var service = CreateService(dualWrite: true);

        var result = await service.AddToQueueAsync(
            _userId,
            new CreateLlmRequestDto(CaptureRequestContract.RequestTypeV1, CapturePayload(CaptureSource.TranscriptPaste, "meeting notes")));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        mirrored.Should().NotBeNull("the enqueue path may not bypass the dual-write seam");
        mirrored!.Id.Should().Be(result.Value!.Id);
        mirrored.LegacyRequestId.Should().Be(result.Value.Id);
        mirrored.UserId.Should().Be(_userId);
        mirrored.LegacySourceSnapshot.Should().Be(CaptureSource.TranscriptPaste);
        mirrored.UserTitle.Should().Be("Enqueued");
        mirrored.SourceAssets.Should().ContainSingle().Which.TextPayload!.Text.Should().Be("meeting notes");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once, "the mirror commits with the queue row");
    }

    [Fact]
    public async Task AddToQueueAsync_CaptureRequest_ShouldMirrorAsOrganizeBecauseClientsCannotSupplyADisposition()
    {
        // The enqueue contract rejects server-attribution fields (a disposition is recorded later by
        // the server), so a freshly enqueued capture is always the default Organize intent — and a
        // client that tries to smuggle one in is refused before anything is staged.
        Capture? mirrored = null;
        _captureStoreMock
            .Setup(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => mirrored = capture)
            .Returns(Task.CompletedTask);
        var service = CreateService(dualWrite: true);
        var disposition = new CaptureDispositionV1(CaptureDisposition.ProposalRequested, DateTimeOffset.UtcNow, _userId);

        var smuggled = await service.AddToQueueAsync(
            _userId,
            new CreateLlmRequestDto(CaptureRequestContract.RequestTypeV1, CapturePayload(CaptureSource.Typed, "act on this", disposition)));
        var plain = await service.AddToQueueAsync(
            _userId,
            new CreateLlmRequestDto(CaptureRequestContract.RequestTypeV1, CapturePayload(CaptureSource.Typed, "organize this")));

        smuggled.IsSuccess.Should().BeFalse("a client-supplied disposition is a server-attribution field");
        plain.IsSuccess.Should().BeTrue(plain.ErrorMessage);
        mirrored!.RequestedIntent.Should().Be(CaptureIntentMode.Organize);
        mirrored.EffectiveIntent.Should().Be(CaptureIntentMode.Organize);
        mirrored.Disposition.Should().Be(CaptureUserDisposition.Active);
        _captureStoreMock.Verify(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddToQueueAsync_NonCaptureRequest_ShouldNeverTouchTheStore()
    {
        var service = CreateService(dualWrite: true);

        var result = await service.AddToQueueAsync(_userId, new CreateLlmRequestDto("chat.completion.v1", "{\"prompt\":\"hi\"}"));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _captureStoreMock.Verify(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddToQueueAsync_WithDualWriteDisabled_ShouldNeverTouchTheStore()
    {
        var service = CreateService(dualWrite: false);

        var result = await service.AddToQueueAsync(
            _userId,
            new CreateLlmRequestDto(CaptureRequestContract.RequestTypeV1, CapturePayload(CaptureSource.Typed, "typed note")));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _captureStoreMock.Verify(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task AddToQueueAsync_WithTheLegacyConstructor_ShouldBehaveAsBefore()
    {
        var service = new LlmQueueService(_unitOfWorkMock.Object, _authorizationServiceMock.Object);

        var result = await service.AddToQueueAsync(
            _userId,
            new CreateLlmRequestDto(CaptureRequestContract.RequestTypeV1, CapturePayload(CaptureSource.Paste, "pasted note")));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Theory]
    [InlineData(CaptureDisposition.ProposalRequested, CaptureIntentMode.Act)]
    [InlineData(CaptureDisposition.Kept, CaptureIntentMode.Remember)]
    [InlineData(CaptureDisposition.Archived, CaptureIntentMode.Organize)]
    public void ResolveRequestedIntent_ShouldMapTheLegacyDisposition(CaptureDisposition legacy, CaptureIntentMode expected)
    {
        var disposition = new CaptureDispositionV1(legacy, DateTimeOffset.UtcNow, Guid.NewGuid());

        CaptureIntakeService.ResolveRequestedIntent(disposition).Should().Be(expected);
        CaptureIntakeService.ResolveRequestedIntent(null).Should().Be(CaptureIntentMode.Organize);
    }
}
