using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OpsCliServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ICommandRunRepository> _commandRunRepoMock = new();
    private readonly Mock<IBoardRepository> _boardRepoMock = new();
    private readonly Mock<ILlmQueueRepository> _queueRepoMock = new();
    private readonly InMemoryLogger<OpsCliService> _logger = new();
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

        _service = new OpsCliService(_unitOfWorkMock.Object, _logger);
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
        result.ErrorMessage.Should().Contain("requires role 'admin'");
        result.ErrorMessage.Should().Contain("current role is 'editor'");
        result.ErrorMessage.Should().Contain("Runnable templates for your role: health.check");
        result.ErrorMessage.Should().Contain("Workspace > Settings");
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
    public async Task RunCommandAsync_QueuePending_UsesBoundedDisplayRead_CappedAtFifty()
    {
        // #1250 review: lock the queue.pending listing to the bounded display read (cap 50) so the
        // limit constant can't silently drift back to the unbounded full-backlog GetByStatusAsync.
        var userId = Guid.NewGuid();
        var adminUser = new User("admin", "admin-queue@example.com", "hash", UserRole.Admin);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(adminUser);

        var newest = new LlmRequest(userId, "automation.command", "{\"n\":1}");
        var older = new LlmRequest(userId, "automation.command", "{\"o\":1}");
        _queueRepoMock
            .Setup(r => r.GetByStatusForDisplayAsync(RequestStatus.Pending, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { newest, older });

        var result = await _service.RunCommandAsync(userId, new RunCommandDto("queue.pending"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.OutputPreview.Should().Contain("Pending queue items: 2");
        result.Value.OutputPreview.Should().Contain(newest.Id.ToString());
        // The bounded display read is used; the unbounded full-backlog read is never called.
        _queueRepoMock.Verify(r => r.GetByStatusForDisplayAsync(RequestStatus.Pending, 50, It.IsAny<CancellationToken>()), Times.Once);
        _queueRepoMock.Verify(r => r.GetByStatusAsync(It.IsAny<RequestStatus>(), It.IsAny<CancellationToken>()), Times.Never);
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
        result.ErrorMessage.Should().Be("Unsupported parameter(s) for template 'health.check': bad");
    }

    [Fact]
    public async Task RunCommandAsync_ShouldNotPersistOrReturnUnknownExceptionDetails()
    {
        var userId = Guid.NewGuid();
        var adminUser = new User("admin", "admin-unknown-error@example.com", "hash", UserRole.Admin);
        const string correlationId = "ops-cli-safe-failure-correlation";
        const string secretMarker = "raw-ops-token-7f91";
        const string pathMarker = @"C:\tenant\db.sqlite";
        const string sqlMarker = "UNIQUE constraint failed: Boards.Name";
        const string providerMarker = "https://provider.example/internal?api_key=private";
        var exception = new InvalidOperationException(
            $"token={secretMarker} path={pathMarker} sql={sqlMarker} provider={providerMarker}");
        CommandRun? persistedRun = null;

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(adminUser);
        _boardRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        _commandRunRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CommandRun>(), default))
            .Callback<CommandRun, CancellationToken>((run, _) => persistedRun = run)
            .ReturnsAsync((CommandRun run, CancellationToken _) => run);

        var result = await _service.RunCommandAsync(
            userId,
            new RunCommandDto("boards.list"),
            correlationId,
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CommandRunStatus.Failed);
        result.Value.CorrelationId.Should().Be(correlationId);

        persistedRun.Should().NotBeNull();
        persistedRun!.Status.Should().Be(CommandRunStatus.Failed);
        persistedRun.CorrelationId.Should().Be(correlationId);
        var persistedErrorLog = persistedRun.Logs.Single(log => log.Level == "Error");
        var publicMessagesAreGeneric = string.Equals(
                result.Value.ErrorMessage,
                SensitiveDataRedactor.GenericUnexpectedFailureMessage,
                StringComparison.Ordinal)
            && string.Equals(
                persistedRun.ErrorMessage,
                SensitiveDataRedactor.GenericUnexpectedFailureMessage,
                StringComparison.Ordinal)
            && string.Equals(
                persistedErrorLog.Message,
                SensitiveDataRedactor.GenericUnexpectedFailureMessage,
                StringComparison.Ordinal);
        publicMessagesAreGeneric.Should().BeTrue(
            "unknown failures must use the stable content-free message on every persisted and returned surface");

        var publicAndPersistedText = string.Join(
            "\n",
            new[] { result.Value.ErrorMessage, persistedRun.ErrorMessage }
                .Concat(persistedRun.Logs.Select(log => log.Message)));
        var leakedMarkerCount = new[] { secretMarker, pathMarker, sqlMarker, providerMarker }
            .Count(marker => publicAndPersistedText.Contains(marker, StringComparison.Ordinal));
        leakedMarkerCount.Should().Be(0,
            "a failed regression must not render any secret-like canary in test output");

        var protectedLog = _logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        ReferenceEquals(protectedLog.Exception, exception).Should().BeTrue();
        protectedLog.Message.Should().Contain(persistedRun.Id.ToString());
        protectedLog.Message.Should().Contain(correlationId);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Exactly(2));
    }

    [Fact]
    public async Task RunCommandAsync_ShouldPreserveDeliberateDomainFailureMessage()
    {
        var userId = Guid.NewGuid();
        var adminUser = new User("admin", "admin-domain-error@example.com", "hash", UserRole.Admin);
        const string safeDomainMessage = "The board catalogue is unavailable.";
        CommandRun? persistedRun = null;

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(adminUser);
        _boardRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(ErrorCodes.InvalidOperation, safeDomainMessage));
        _commandRunRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CommandRun>(), default))
            .Callback<CommandRun, CancellationToken>((run, _) => persistedRun = run)
            .ReturnsAsync((CommandRun run, CancellationToken _) => run);

        var result = await _service.RunCommandAsync(
            userId,
            new RunCommandDto("boards.list"),
            "ops-cli-domain-failure-correlation",
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CommandRunStatus.Failed);
        result.Value.ErrorMessage.Should().Be(safeDomainMessage);
        persistedRun.Should().NotBeNull();
        persistedRun!.ErrorMessage.Should().Be(safeDomainMessage);
        persistedRun.Logs.Should().ContainSingle(log =>
            log.Level == "Error" && log.Message.Contains(safeDomainMessage, StringComparison.Ordinal));
        _logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Error);
    }
}
