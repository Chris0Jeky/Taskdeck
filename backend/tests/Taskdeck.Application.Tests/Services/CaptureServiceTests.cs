using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CaptureServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAuthorizationService> _authorizationServiceMock;
    private readonly Mock<ILlmQueueRepository> _llmQueueRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly CaptureService _service;

    public CaptureServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _llmQueueRepositoryMock = new Mock<ILlmQueueRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();

        _unitOfWorkMock.SetupGet(u => u.LlmQueue).Returns(_llmQueueRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Users).Returns(_userRepositoryMock.Object);

        _service = new CaptureService(_unitOfWorkMock.Object, _authorizationServiceMock.Object);
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistCaptureRequestAndReturnDetail()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("capture-user", "capture-user@example.com", "hash");
        var dto = new CreateCaptureItemDto(boardId, "quick capture text", "paste");
        LlmRequest? persisted = null;

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _llmQueueRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<LlmRequest>(), default))
            .Callback<LlmRequest, CancellationToken>((request, _) => persisted = request)
            .ReturnsAsync((LlmRequest request, CancellationToken _) => request);

        var result = await _service.CreateAsync(userId, dto);

        result.IsSuccess.Should().BeTrue();
        persisted.Should().NotBeNull();
        persisted!.RequestType.Should().Be(CaptureRequestContract.RequestTypeV1);
        var parsedPayload = CaptureRequestContract.ParsePayload(persisted.Payload);
        parsedPayload.IsSuccess.Should().BeTrue();
        parsedPayload.Value.Source.Should().Be(CaptureSource.Paste);
        parsedPayload.Value.Text.Should().Be("quick capture text");
        result.Value.RawText.Should().Be("quick capture text");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnValidationError_WhenSourceIsInvalid()
    {
        var userId = Guid.NewGuid();
        var user = new User("capture-user", "capture-user@example.com", "hash");
        var dto = new CreateCaptureItemDto(null, "quick capture text", "unknown-source");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);

        var result = await _service.CreateAsync(userId, dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Invalid capture source");
        _llmQueueRepositoryMock.Verify(r => r.AddAsync(It.IsAny<LlmRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnForbidden_WhenBoardAccessIsDenied()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("capture-user", "capture-user@example.com", "hash");
        var dto = new CreateCaptureItemDto(boardId, "quick capture text");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await _service.CreateAsync(userId, dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnOnlyCaptureRequestsAndApplyStatusFilter()
    {
        var userId = Guid.NewGuid();
        var capturePending = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "pending text");
        var captureCancelled = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "cancelled text");
        captureCancelled.Cancel();
        var nonCapture = new LlmRequest(userId, "summarize", "queue payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(new[] { capturePending, captureCancelled, nonCapture });

        var result = await _service.ListAsync(
            userId,
            new CaptureListFilterDto(Status: CaptureStatus.Ignored));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Id.Should().Be(captureCancelled.Id);
        result.Value[0].Status.Should().Be(CaptureStatus.Ignored);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnForbidden_WhenCaptureBelongsToDifferentUser()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var item = new LlmRequest(ownerId, CaptureRequestContract.RequestTypeV1, "capture payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.GetByIdAsync(callerId, item.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task IgnoreAsync_ShouldBeIdempotent_WhenAlreadyCancelled()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");
        item.Cancel();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.IgnoreAsync(userId, item.Id);

        result.IsSuccess.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }
}
