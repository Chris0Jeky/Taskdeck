using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CaptureBoardAttachmentAuthorizationTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILlmQueueRepository> _llmQueue = new();
    private readonly User _user = new("capture-viewer", "capture-viewer@example.com", "Password1!");

    public CaptureBoardAttachmentAuthorizationTests()
    {
        _unitOfWork.SetupGet(unit => unit.Users).Returns(_users.Object);
        _unitOfWork.SetupGet(unit => unit.LlmQueue).Returns(_llmQueue.Object);
        _users
            .Setup(repository => repository.GetByIdAsync(
                _user.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_user);
        _llmQueue
            .Setup(repository => repository.AddAsync(
                It.IsAny<LlmRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmRequest request, CancellationToken _) => request);
        _unitOfWork
            .Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private CaptureService BuildService() => new(
        _unitOfWork.Object,
        _authorization.Object);

    [Fact]
    public async Task CreateAsync_RejectsReadableBoard_WhenCallerCannotWrite()
    {
        var boardId = Guid.NewGuid();
        _authorization
            .Setup(service => service.CanReadBoardAsync(_user.Id, boardId))
            .ReturnsAsync(Result.Success(true));
        _authorization
            .Setup(service => service.CanWriteBoardAsync(_user.Id, boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await BuildService().CreateAsync(
            _user.Id,
            new CreateCaptureItemDto(boardId, "Viewer must not attach this capture", "paste"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _authorization.Verify(
            service => service.CanWriteBoardAsync(_user.Id, boardId),
            Times.Once);
        _llmQueue.Verify(
            repository => repository.AddAsync(
                It.IsAny<LlmRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_PropagatesWriteAuthorizationFailure_WithoutPersistence()
    {
        var boardId = Guid.NewGuid();
        _authorization
            .Setup(service => service.CanReadBoardAsync(_user.Id, boardId))
            .ReturnsAsync(Result.Success(true));
        _authorization
            .Setup(service => service.CanWriteBoardAsync(_user.Id, boardId))
            .ReturnsAsync(Result.Failure<bool>(
                ErrorCodes.NotFound,
                "Board not found"));

        var result = await BuildService().CreateAsync(
            _user.Id,
            new CreateCaptureItemDto(boardId, "Missing board", "paste"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Be("Board not found");
        _llmQueue.Verify(
            repository => repository.AddAsync(
                It.IsAny<LlmRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_AllowsWritableBoard_WithoutConsultingReadPermission()
    {
        var boardId = Guid.NewGuid();
        _authorization
            .Setup(service => service.CanWriteBoardAsync(_user.Id, boardId))
            .ReturnsAsync(Result.Success(true));
        _authorization
            .Setup(service => service.CanReadBoardAsync(_user.Id, boardId))
            .ThrowsAsync(new InvalidOperationException("Read permission is not the attachment contract"));

        var result = await BuildService().CreateAsync(
            _user.Id,
            new CreateCaptureItemDto(boardId, "Editor may attach this capture", "paste"));

        result.IsSuccess.Should().BeTrue();
        result.Value.BoardId.Should().Be(boardId);
        _authorization.Verify(
            service => service.CanReadBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
            Times.Never);
        _llmQueue.Verify(
            repository => repository.AddAsync(
                It.Is<LlmRequest>(request =>
                    request.UserId == _user.Id && request.BoardId == boardId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
