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

public class OpsCliServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ICommandRunRepository> _commandRunRepoMock = new();
    private readonly Mock<IBoardRepository> _boardRepoMock = new();
    private readonly Mock<ILlmQueueRepository> _queueRepoMock = new();
    private readonly OpsCliService _service;

    public OpsCliServiceTests()
    {
        _unitOfWorkMock.SetupGet(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.CommandRuns).Returns(_commandRunRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.LlmQueue).Returns(_queueRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        _commandRunRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CommandRun>(), default))
            .ReturnsAsync((CommandRun run, CancellationToken _) => run);

        _service = new OpsCliService(_unitOfWorkMock.Object);
    }

    [Fact]
    public async Task RunCommandAsync_ShouldReturnForbidden_WhenUserRoleIsInsufficient()
    {
        var userId = Guid.NewGuid();
        var editorUser = new User("editor", "editor@example.com", "hash", UserRole.Editor);

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(editorUser);

        var result = await _service.RunCommandAsync(userId, new RunCommandDto("boards.list"), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task RunCommandAsync_ShouldSucceed_ForHealthCheckTemplate()
    {
        var userId = Guid.NewGuid();
        var editorUser = new User("editor", "editor2@example.com", "hash", UserRole.Editor);

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(editorUser);

        var result = await _service.RunCommandAsync(userId, new RunCommandDto("health.check"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.OutputPreview.Should().Contain("Health check: OK");
    }

    [Fact]
    public async Task RunCommandAsync_ShouldReturnValidationError_ForUnknownTemplateParameter()
    {
        var userId = Guid.NewGuid();
        var editorUser = new User("editor", "editor3@example.com", "hash", UserRole.Editor);

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(editorUser);

        var result = await _service.RunCommandAsync(
            userId,
            new RunCommandDto("health.check", new Dictionary<string, string> { ["bad"] = "value" }),
            default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }
}
