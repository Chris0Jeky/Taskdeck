using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OpsCliServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ICommandRunRepository> _commandRunRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<ILlmQueueRepository> _llmQueueRepoMock;
    private readonly OpsCliService _service;

    public OpsCliServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _commandRunRepoMock = new Mock<ICommandRunRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _llmQueueRepoMock = new Mock<ILlmQueueRepository>();

        _unitOfWorkMock.Setup(u => u.CommandRuns).Returns(_commandRunRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.LlmQueue).Returns(_llmQueueRepoMock.Object);

        _service = new OpsCliService(_unitOfWorkMock.Object);
    }

    #region RunCommandAsync Tests

    [Fact]
    public async Task RunCommandAsync_ShouldReturnSuccess_ForValidTemplate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new RunCommandDto("health.check");

        _commandRunRepoMock.Setup(r => r.AddAsync(It.IsAny<CommandRun>(), default))
            .ReturnsAsync((CommandRun r, CancellationToken ct) => r);

        // Act
        var result = await _service.RunCommandAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TemplateName.Should().Be("health.check");
        result.Value.Status.Should().Be(CommandRunStatus.Completed);
        result.Value.ExitCode.Should().Be(0);
        result.Value.OutputPreview.Should().Contain("Health check: OK");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RunCommandAsync_ShouldReturnValidationError_ForUnknownTemplate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new RunCommandDto("unknown.command");

        // Act
        var result = await _service.RunCommandAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unknown command template");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task RunCommandAsync_BoardsList_ShouldReturnBoardList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boards = new List<Board>
        {
            new Board("Board One"),
            new Board("Board Two")
        };
        var dto = new RunCommandDto("boards.list");

        _commandRunRepoMock.Setup(r => r.AddAsync(It.IsAny<CommandRun>(), default))
            .ReturnsAsync((CommandRun r, CancellationToken ct) => r);
        _boardRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(boards);

        // Act
        var result = await _service.RunCommandAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TemplateName.Should().Be("boards.list");
        result.Value.Status.Should().Be(CommandRunStatus.Completed);
        result.Value.OutputPreview.Should().Contain("Found 2 board(s)");
        result.Value.OutputPreview.Should().Contain("Board One");
        result.Value.OutputPreview.Should().Contain("Board Two");
    }

    #endregion

    #region GetCommandRunAsync Tests

    [Fact]
    public async Task GetCommandRunAsync_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var runId = Guid.NewGuid();

        _commandRunRepoMock.Setup(r => r.GetByIdWithLogsAsync(runId, default))
            .ReturnsAsync((CommandRun?)null);

        // Act
        var result = await _service.GetCommandRunAsync(runId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetCommandRunAsync_ShouldReturnDetail_WhenExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var commandRun = new CommandRun("health.check", userId, Guid.NewGuid().ToString("N"));

        _commandRunRepoMock.Setup(r => r.GetByIdWithLogsAsync(commandRun.Id, default))
            .ReturnsAsync(commandRun);

        // Act
        var result = await _service.GetCommandRunAsync(commandRun.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(commandRun.Id);
        result.Value.TemplateName.Should().Be("health.check");
        result.Value.RequestedByUserId.Should().Be(userId);
    }

    #endregion

    #region GetCommandRunLogsAsync Tests

    [Fact]
    public async Task GetCommandRunLogsAsync_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var runId = Guid.NewGuid();

        _commandRunRepoMock.Setup(r => r.GetByIdWithLogsAsync(runId, default))
            .ReturnsAsync((CommandRun?)null);

        // Act
        var result = await _service.GetCommandRunLogsAsync(runId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region GetAvailableTemplates Tests

    [Fact]
    public void GetAvailableTemplates_ShouldReturnAllTemplates()
    {
        // Act
        var result = _service.GetAvailableTemplates();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var templates = result.Value.ToList();
        templates.Should().HaveCount(5);
        templates.Select(t => t.Name).Should().Contain("health.check");
        templates.Select(t => t.Name).Should().Contain("boards.list");
        templates.Select(t => t.Name).Should().Contain("boards.search");
        templates.Select(t => t.Name).Should().Contain("queue.stats");
        templates.Select(t => t.Name).Should().Contain("queue.pending");
    }

    #endregion
}
