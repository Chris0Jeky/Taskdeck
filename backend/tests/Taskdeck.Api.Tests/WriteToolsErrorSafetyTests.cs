using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// #2351 — MCP write tools must not echo unknown-exception text back to the model.
/// Every directly returned failed <see cref="Result"/> goes through
/// <see cref="SensitiveDataRedactor.SanitizeLlmFailureMessage"/>: an
/// <c>UnexpectedError</c> collapses to the stable generic message, while known domain,
/// validation, conflict, and authorization strings stay byte-for-byte unchanged.
/// </summary>
public class WriteToolsErrorSafetyTests
{
    private const string HostileError =
        "Bearer sk-live-ABC123 C:\\Users\\alice\\AppData\\taskdeck.db " +
        "SQLite Error 19: UNIQUE constraint failed: Users.Email " +
        "https://provider.example/v1/internal";

    private static readonly string[] HostileMarkers =
    [
        "sk-live-ABC123",
        "C:\\Users\\alice",
        // JSON-escapes the backslashes, so assert the unescaped fragment too.
        "alice",
        "taskdeck.db",
        "SQLite Error 19",
        "UNIQUE constraint failed",
        "https://provider.example/v1/internal"
    ];

    // ── canWrite: unexpected authorization failure ───────────────────────────

    [Fact]
    public async Task CreateCard_UnexpectedAuthorizationFailure_ReturnsGenericError()
    {
        var boardId = Guid.NewGuid();
        var authorization = FailingAuthorization(boardId, ErrorCodes.UnexpectedError, HostileError);

        var json = await CreateTools(authorization: authorization.Object)
            .CreateCard(boardId.ToString(), "Title");

        AssertGenericError(json);
    }

    [Fact]
    public async Task CreateColumn_UnexpectedAuthorizationFailure_ReturnsGenericError()
    {
        var boardId = Guid.NewGuid();
        var authorization = FailingAuthorization(boardId, ErrorCodes.UnexpectedError, HostileError);

        var json = await CreateTools(authorization: authorization.Object)
            .CreateColumn(boardId.ToString(), "Doing");

        AssertGenericError(json);
    }

    [Fact]
    public async Task CreateCard_KnownAuthorizationFailure_PreservesStableMessage()
    {
        const string stableMessage = "Board not found.";
        var boardId = Guid.NewGuid();
        var authorization = FailingAuthorization(boardId, ErrorCodes.NotFound, stableMessage);

        var json = await CreateTools(authorization: authorization.Object)
            .CreateCard(boardId.ToString(), "Title");

        ReadError(json).Should().Be(stableMessage);
    }

    // ── canWrite false: authorization literals are not Results ───────────────

    [Fact]
    public async Task CreateCard_NotAuthorized_PreservesLiteralMessage()
    {
        var boardId = Guid.NewGuid();

        var json = await CreateTools(authorization: AllowingAuthorization(boardId, canWrite: false).Object)
            .CreateCard(boardId.ToString(), "Title");

        ReadError(json).Should().Be("Not authorized to create cards on this board");
    }

    [Fact]
    public async Task CreateColumn_NotAuthorized_PreservesLiteralMessage()
    {
        var boardId = Guid.NewGuid();

        var json = await CreateTools(authorization: AllowingAuthorization(boardId, canWrite: false).Object)
            .CreateColumn(boardId.ToString(), "Doing");

        ReadError(json).Should().Be("Not authorized to create columns on this board");
    }

    // ── proposal creation results ────────────────────────────────────────────

    [Fact]
    public async Task CreateCard_UnexpectedProposalFailure_ReturnsGenericError()
    {
        var boardId = Guid.NewGuid();
        var unitOfWork = UnitOfWorkWithColumns(boardId, new Column(boardId, "Todo", 0));

        var json = await CreateTools(
                proposalService: FailingProposalService(ErrorCodes.UnexpectedError, HostileError).Object,
                unitOfWork: unitOfWork.Object,
                authorization: AllowingAuthorization(boardId).Object)
            .CreateCard(boardId.ToString(), "Title");

        AssertGenericError(json);
    }

    [Fact]
    public async Task CreateCard_KnownProposalFailure_PreservesStableMessage()
    {
        const string stableMessage = "Board not found.";
        var boardId = Guid.NewGuid();
        var unitOfWork = UnitOfWorkWithColumns(boardId, new Column(boardId, "Todo", 0));

        var json = await CreateTools(
                proposalService: FailingProposalService(ErrorCodes.NotFound, stableMessage).Object,
                unitOfWork: unitOfWork.Object,
                authorization: AllowingAuthorization(boardId).Object)
            .CreateCard(boardId.ToString(), "Title");

        ReadError(json).Should().Be(stableMessage);
    }

    [Fact]
    public async Task MoveCard_UnexpectedProposalFailure_ReturnsGenericError()
    {
        var json = await CreateTools(
                proposalService: FailingProposalService(ErrorCodes.UnexpectedError, HostileError).Object)
            .MoveCard(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        AssertGenericError(json);
    }

    [Fact]
    public async Task MoveCard_KnownProposalFailure_PreservesStableMessage()
    {
        const string stableMessage = "Card not found.";

        var json = await CreateTools(
                proposalService: FailingProposalService(ErrorCodes.NotFound, stableMessage).Object)
            .MoveCard(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        ReadError(json).Should().Be(stableMessage);
    }

    [Fact]
    public async Task UpdateCard_UnexpectedProposalFailure_ReturnsGenericError()
    {
        var json = await CreateTools(
                proposalService: FailingProposalService(ErrorCodes.UnexpectedError, HostileError).Object)
            .UpdateCard(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), title: "New title");

        AssertGenericError(json);
    }

    [Fact]
    public async Task ArchiveCard_UnexpectedProposalFailure_ReturnsGenericError()
    {
        var json = await CreateTools(
                proposalService: FailingProposalService(ErrorCodes.UnexpectedError, HostileError).Object)
            .ArchiveCard(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        AssertGenericError(json);
    }

    [Fact]
    public async Task ArchiveCard_ForbiddenProposalFailure_PreservesStableMessage()
    {
        const string stableMessage = "You do not have access to this board.";

        var json = await CreateTools(
                proposalService: FailingProposalService(ErrorCodes.Forbidden, stableMessage).Object)
            .ArchiveCard(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        ReadError(json).Should().Be(stableMessage);
    }

    [Fact]
    public async Task CreateColumn_UnexpectedProposalFailure_ReturnsGenericError()
    {
        var boardId = Guid.NewGuid();
        var unitOfWork = UnitOfWorkWithColumns(boardId);

        var json = await CreateTools(
                proposalService: FailingProposalService(ErrorCodes.UnexpectedError, HostileError).Object,
                unitOfWork: unitOfWork.Object,
                authorization: AllowingAuthorization(boardId).Object)
            .CreateColumn(boardId.ToString(), "Doing");

        AssertGenericError(json);
    }

    // ── capture result ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCapture_UnexpectedFailure_ReturnsGenericError()
    {
        var captureService = new Mock<ICaptureService>(MockBehavior.Strict);
        captureService
            .Setup(service => service.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<CreateCaptureItemDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CaptureItemDto>(ErrorCodes.UnexpectedError, HostileError));

        var json = await CreateTools(captureService: captureService.Object).CreateCapture("An idea");

        AssertGenericError(json);
    }

    [Fact]
    public async Task CreateCapture_ValidationFailure_PreservesStableMessage()
    {
        const string stableMessage = "Capture text cannot be empty.";
        var captureService = new Mock<ICaptureService>(MockBehavior.Strict);
        captureService
            .Setup(service => service.CreateAsync(
                It.IsAny<Guid>(),
                It.IsAny<CreateCaptureItemDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CaptureItemDto>(ErrorCodes.ValidationError, stableMessage));

        var json = await CreateTools(captureService: captureService.Object).CreateCapture("An idea");

        ReadError(json).Should().Be(stableMessage);
    }

    // ── static validator results (conflict / validation) ─────────────────────

    [Fact]
    public async Task CreateColumn_AppendPositionConflict_PreservesStableMessage()
    {
        var boardId = Guid.NewGuid();
        var unitOfWork = UnitOfWorkWithColumns(boardId, new Column(boardId, "Last", int.MaxValue));

        var json = await CreateTools(
                unitOfWork: unitOfWork.Object,
                authorization: AllowingAuthorization(boardId).Object)
            .CreateColumn(boardId.ToString(), "Doing");

        ReadError(json).Should()
            .Be("Cannot append a column because the board has no higher position available");
    }

    [Fact]
    public async Task CreateColumn_ContractValidationFailure_PreservesStableMessage()
    {
        var boardId = Guid.NewGuid();
        var unitOfWork = UnitOfWorkWithColumns(boardId);

        var json = await CreateTools(
                unitOfWork: unitOfWork.Object,
                authorization: AllowingAuthorization(boardId).Object)
            .CreateColumn(boardId.ToString(), "   ");

        var error = ReadError(json);
        error.Should().NotBe(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        error.Should().NotBeNullOrWhiteSpace();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void AssertGenericError(string json)
    {
        var error = ReadError(json);
        error.Should().Be(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        foreach (var marker in HostileMarkers)
            json.Should().NotContain(marker);
    }

    private static string? ReadError(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("error").GetString();
    }

    private static Mock<IAuthorizationService> FailingAuthorization(
        Guid boardId,
        string errorCode,
        string errorMessage)
    {
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization
            .Setup(service => service.CanWriteBoardAsync(It.IsAny<Guid>(), boardId))
            .ReturnsAsync(Result.Failure<bool>(errorCode, errorMessage));
        return authorization;
    }

    private static Mock<IAuthorizationService> AllowingAuthorization(Guid boardId, bool canWrite = true)
    {
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization
            .Setup(service => service.CanWriteBoardAsync(It.IsAny<Guid>(), boardId))
            .ReturnsAsync(Result.Success(canWrite));
        return authorization;
    }

    private static Mock<IAutomationProposalService> FailingProposalService(
        string errorCode,
        string errorMessage)
    {
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.CreateProposalAsync(
                It.IsAny<CreateProposalDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProposalDto>(errorCode, errorMessage));
        return proposalService;
    }

    private static Mock<IUnitOfWork> UnitOfWorkWithColumns(Guid boardId, params Column[] columns)
    {
        var columnRepository = new Mock<IColumnRepository>(MockBehavior.Strict);
        columnRepository
            .Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(columns);

        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWork.SetupGet(work => work.Columns).Returns(columnRepository.Object);
        return unitOfWork;
    }

    private static WriteTools CreateTools(
        IAutomationProposalService? proposalService = null,
        ICaptureService? captureService = null,
        IUnitOfWork? unitOfWork = null,
        IAuthorizationService? authorization = null)
    {
        return new WriteTools(
            proposalService ?? new Mock<IAutomationProposalService>(MockBehavior.Strict).Object,
            new FixedUserContextProvider(Guid.NewGuid()),
            captureService ?? new Mock<ICaptureService>(MockBehavior.Strict).Object,
            unitOfWork ?? new Mock<IUnitOfWork>(MockBehavior.Strict).Object,
            authorization ?? new Mock<IAuthorizationService>(MockBehavior.Strict).Object);
    }

    private sealed class FixedUserContextProvider(Guid userId) : IUserContextProvider
    {
        public Task<McpUserContext> GetCurrentContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new McpUserContext(userId, ApiKeyScope.Full));

        public Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(userId);

        public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(userId);
    }
}
