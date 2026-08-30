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
/// ADR-0065 §Decision 1 / CF-01 (#2255): while <c>ContextFabric:DualWriteCaptures</c> is on, every
/// new capture is mirrored into the durable aggregate under the queue row's own id; while it is off
/// (the default), the service is byte-for-byte the pre-ADR-0065 service.
/// </summary>
public sealed class CaptureServiceDualWriteTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();
    private readonly Mock<ILlmQueueRepository> _llmQueueRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ICaptureStore> _captureStoreMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    public CaptureServiceDualWriteTests()
    {
        _unitOfWorkMock.SetupGet(u => u.LlmQueue).Returns(_llmQueueRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Users).Returns(_userRepositoryMock.Object);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId, default))
            .ReturnsAsync(new User("capture-user", "capture-user@example.com", "hash"));
        _llmQueueRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmRequest request, CancellationToken _) => request);
    }

    private CaptureService CreateService(bool dualWrite) =>
        new(
            _unitOfWorkMock.Object,
            _authorizationServiceMock.Object,
            _captureStoreMock.Object,
            new ContextFabricSettings { DualWriteCaptures = dualWrite });

    [Fact]
    public async Task CreateAsync_WithDualWriteEnabled_ShouldMirrorTheCaptureUnderTheQueueRowId()
    {
        Capture? mirrored = null;
        _captureStoreMock
            .Setup(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => mirrored = capture)
            .Returns(Task.CompletedTask);
        var service = CreateService(dualWrite: true);

        var result = await service.CreateAsync(
            _userId,
            new CreateCaptureItemDto(null, "remember to book the venue", "voice", TitleHint: "  Venue  "));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        mirrored.Should().NotBeNull();
        mirrored!.Id.Should().Be(result.Value!.Id, "the mirror is ID-preserving");
        mirrored.LegacyRequestId.Should().Be(result.Value.Id);
        mirrored.UserId.Should().Be(_userId);
        mirrored.LegacySource.Should().Be(CaptureSource.Voice);
        mirrored.PrimaryModality.Should().Be(CaptureModality.Audio);
        mirrored.OriginAdapter.Should().Be(CaptureOriginAdapter.WebComposer);
        mirrored.Producer.Should().Be(CaptureProducerKind.Human);
        mirrored.Intent.Should().Be(CaptureIntentMode.Organize);
        mirrored.Lifecycle.Should().Be(CaptureLifecycleState.Received);
        mirrored.ContextBoardId.Should().BeNull();
        mirrored.UserTitle.Should().Be("Venue");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once, "the mirror commits with the queue row");
    }

    [Theory]
    [InlineData("markdownImport", CaptureProducerKind.Import, CaptureOriginAdapter.Import, CaptureModality.Document)]
    [InlineData("import", CaptureProducerKind.Import, CaptureOriginAdapter.Import, CaptureModality.Document)]
    [InlineData("meetingIntegration", CaptureProducerKind.Integration, CaptureOriginAdapter.Integration, CaptureModality.Text)]
    [InlineData("vsCodeExtension", CaptureProducerKind.Human, CaptureOriginAdapter.VsCodeExtension, CaptureModality.Text)]
    public async Task CreateAsync_WithDualWriteEnabled_ShouldStampTheProducerFromTheMappingNotTheCaller(
        string source,
        CaptureProducerKind expectedProducer,
        CaptureOriginAdapter expectedOrigin,
        CaptureModality expectedModality)
    {
        // Review finding on the scaffold PR: the seam must never override the mapping's producer —
        // an ID-preserving mirror with the wrong producer would need a data migration to repair.
        Capture? mirrored = null;
        _captureStoreMock
            .Setup(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => mirrored = capture)
            .Returns(Task.CompletedTask);
        var service = CreateService(dualWrite: true);

        var result = await service.CreateAsync(_userId, new CreateCaptureItemDto(null, "imported text", source));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        mirrored!.Producer.Should().Be(expectedProducer);
        mirrored.OriginAdapter.Should().Be(expectedOrigin);
        mirrored.PrimaryModality.Should().Be(expectedModality);
    }

    [Fact]
    public async Task CreateAsync_WithDualWriteEnabled_ShouldAcceptATitleHintTheLegacyContractAccepts()
    {
        Capture? mirrored = null;
        _captureStoreMock
            .Setup(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => mirrored = capture)
            .Returns(Task.CompletedTask);
        var service = CreateService(dualWrite: true);

        var result = await service.CreateAsync(
            _userId,
            new CreateCaptureItemDto(null, "typed note", "typed", TitleHint: "clipped\r\npage title"));

        result.IsSuccess.Should().BeTrue("a capture the legacy contract accepts must not be rejected by the mirror");
        mirrored!.UserTitle.Should().Be("clipped  page title", "titles are single-line; control characters become spaces");
    }

    [Fact]
    public async Task CreateAsync_WithDualWriteEnabled_ShouldStampTheQueueRowsIntakeTime()
    {
        LlmRequest? persisted = null;
        Capture? mirrored = null;
        _llmQueueRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LlmRequest, CancellationToken>((request, _) => persisted = request)
            .ReturnsAsync((LlmRequest request, CancellationToken _) => request);
        _captureStoreMock
            .Setup(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => mirrored = capture)
            .Returns(Task.CompletedTask);
        var service = CreateService(dualWrite: true);

        await service.CreateAsync(_userId, new CreateCaptureItemDto(null, "typed note", "typed"));

        mirrored!.CapturedAtServer.Should().Be(persisted!.CreatedAt);
        mirrored.CreatedAt.Should().Be(persisted.CreatedAt);
    }

    [Fact]
    public async Task CreateAsync_WithDualWriteEnabled_ShouldCarryTheBoardAsContextHint()
    {
        var boardId = Guid.NewGuid();
        Capture? mirrored = null;
        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(_userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _captureStoreMock
            .Setup(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback<Capture, CancellationToken>((capture, _) => mirrored = capture)
            .Returns(Task.CompletedTask);
        var service = CreateService(dualWrite: true);

        var result = await service.CreateAsync(_userId, new CreateCaptureItemDto(boardId, "typed note", "typed"));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        mirrored!.ContextBoardId.Should().Be(boardId);
        mirrored.PrimaryModality.Should().Be(CaptureModality.Text);
    }

    [Fact]
    public async Task CreateAsync_WithDualWriteDisabled_ShouldNeverTouchTheStore()
    {
        var service = CreateService(dualWrite: false);

        var result = await service.CreateAsync(_userId, new CreateCaptureItemDto(null, "typed note", "typed"));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _captureStoreMock.Verify(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithTheLegacyConstructor_ShouldBehaveAsBefore()
    {
        var service = new CaptureService(_unitOfWorkMock.Object, _authorizationServiceMock.Object);

        var result = await service.CreateAsync(_userId, new CreateCaptureItemDto(null, "typed note", "paste"));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithDualWriteEnabled_ShouldStageTheMirrorBeforeSaving()
    {
        var order = new List<string>();
        _captureStoreMock
            .Setup(s => s.AddAsync(It.IsAny<Capture>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("mirror"))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save"))
            .ReturnsAsync(2);
        var service = CreateService(dualWrite: true);

        await service.CreateAsync(_userId, new CreateCaptureItemDto(null, "typed note", "typed"));

        order.Should().Equal("mirror", "save");
    }
}
