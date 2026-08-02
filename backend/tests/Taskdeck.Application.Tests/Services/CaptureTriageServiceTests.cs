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

public class CaptureTriageServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardsMock;
    private readonly Mock<IColumnRepository> _columnsMock;
    private readonly Mock<IAutomationProposalRepository> _automationProposalsMock;
    private readonly Mock<IAutomationProposalService> _proposalServiceMock;
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock;
    private readonly CaptureTriageService _service;

    public CaptureTriageServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardsMock = new Mock<IBoardRepository>();
        _columnsMock = new Mock<IColumnRepository>();
        _automationProposalsMock = new Mock<IAutomationProposalRepository>();
        _proposalServiceMock = new Mock<IAutomationProposalService>();
        _policyEngineMock = new Mock<IAutomationPolicyEngine>();

        _unitOfWorkMock.SetupGet(u => u.Boards).Returns(_boardsMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Columns).Returns(_columnsMock.Object);
        _unitOfWorkMock.SetupGet(u => u.AutomationProposals).Returns(_automationProposalsMock.Object);
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(
                It.IsAny<ProposalSourceType>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);
        _policyEngineMock.Setup(p => p.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _policyEngineMock.Setup(p => p.ValidateBoardAccessAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _policyEngineMock.Setup(p => p.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _service = new CaptureTriageService(
            _unitOfWorkMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReuseExistingProposal_WhenSourceReferenceAlreadyExists()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var existingTriageRunId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        var existingProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Capture triage",
            RiskLevel.Low,
            existingTriageRunId.ToString(),
            boardId,
            captureId.ToString());
        existingProposal.AddOperation(new AutomationProposalOperation(
            existingProposal.Id,
            sequence: 0,
            actionType: "create",
            targetType: "card",
            parameters: "{\"title\":\"existing\"}",
            idempotencyKey: "existing-op-1"));

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(
                ProposalSourceType.Queue,
                captureId.ToString(),
                default))
            .ReturnsAsync(existingProposal);

        var result = await _service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Should reuse existing proposal"));

        result.IsSuccess.Should().BeTrue();
        result.Value.ProposalId.Should().Be(existingProposal.Id);
        result.Value.TriageRunId.Should().Be(existingTriageRunId);
        result.Value.OperationCount.Should().Be(1);
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldExtractChecklistAndBulletTasks()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
            .ReturnsAsync(Result.Success(new ProposalDto(
                Guid.NewGuid(),
                ProposalSourceType.Queue,
                captureId.ToString(),
                boardId,
                userId,
                ProposalStatus.PendingReview,
                RiskLevel.Low,
                "Capture triage",
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTime.UtcNow.AddDays(1),
                null,
                null,
                null,
                null,
                Guid.NewGuid().ToString(),
                new List<ProposalOperationDto>())));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            """
            - [ ] Write regression tests
            - [x] Update docs
            1. Ship follow-up PR
            """);

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(3);
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionV1);
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        result.Value.Model.Should().Be(CaptureTriageService.TriageModelName);
        createdProposal.Should().NotBeNull();
        var created = createdProposal!;
        created.SourceType.Should().Be(ProposalSourceType.Queue);
        created.SourceReferenceId.Should().Be(captureId.ToString());
        created.Operations.Should().HaveCount(3);
        created.Operations.Should().OnlyContain(operation => !string.IsNullOrWhiteSpace(operation.TargetId));
        created.Operations![0].Parameters.Should().Contain("Write regression tests");
        created.Operations[1].Parameters.Should().Contain("Update docs");
        created.Operations[2].Parameters.Should().Contain("Ship follow-up PR");
        created.Operations.Select(operation => Guid.TryParse(operation.TargetId, out _)).Should().OnlyContain(parsed => parsed);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRecordDeterministicExtractorProvenance_NotAnLlmProvider()
    {
        // #1273: capture triage is a deterministic, offline text extractor and never calls an LLM,
        // so its provenance must name the extractor itself — not the configured live LLM provider.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var result = await _service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Deterministic provenance"));

        result.IsSuccess.Should().BeTrue();
        // Intentional literals (not the constants): this is the single wire-contract lock that pins the
        // exact strings persisted onto the capture payload / returned by the capture API. A change to the
        // TriageProviderName/TriageModelName constants is an observable contract change and must fail here.
        result.Value.Provider.Should().Be("deterministic-extractor");
        result.Value.Model.Should().Be("capture-triage-v1");
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionV1);
        // Provenance values must satisfy the capture provenance length contract the worker enforces.
        result.Value.Provider.Length.Should().BeLessThanOrEqualTo(CaptureRequestContract.MaxProviderLength);
        result.Value.Model.Length.Should().BeLessThanOrEqualTo(CaptureRequestContract.MaxModelLength);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReuseExistingProposalProvenance_AsDeterministicExtractor()
    {
        // The reuse (already-triaged) branch must record the same deterministic provenance.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        var existingProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Capture triage",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            captureId.ToString());
        existingProposal.AddOperation(new AutomationProposalOperation(
            existingProposal.Id,
            sequence: 0,
            actionType: "create",
            targetType: "card",
            parameters: "{\"title\":\"existing\"}",
            idempotencyKey: "existing-op-1"));

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), default))
            .ReturnsAsync(existingProposal);

        var result = await _service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Reuse existing"));

        result.IsSuccess.Should().BeTrue();
        result.Value.ProposalId.Should().Be(existingProposal.Id);
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        result.Value.Model.Should().Be(CaptureTriageService.TriageModelName);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReturnValidationError_WhenPayloadIsNull()
    {
        var result = await _service.CreateProposalFromCaptureAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            payload: null!);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("payload cannot be null");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldExtractAcmeOnboardingChecklistDeterministically()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Client Onboarding Demo", ownerId: userId);
        var column = new Column(boardId, "New Intake", 0);
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            """
            New client onboarding - ACME Ltd

            - Request director ID documents
            - Send engagement letter
            - Ask for prior year accounts
            - Request bookkeeping / software access
            - Schedule onboarding call
            - Confirm which records are still missing
            - Prepare internal review once documents arrive
            """);

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(7);
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().HaveCount(7);
        createdProposal.Operations![0].Parameters.Should().Contain("Request director ID documents");
        createdProposal.Operations[1].Parameters.Should().Contain("Send engagement letter");
        createdProposal.Operations[2].Parameters.Should().Contain("Ask for prior year accounts");
        createdProposal.Operations[3].Parameters.Should().Contain("Request bookkeeping / software access");
        createdProposal.Operations[4].Parameters.Should().Contain("Schedule onboarding call");
        createdProposal.Operations[5].Parameters.Should().Contain("Confirm which records are still missing");
        createdProposal.Operations[6].Parameters.Should().Contain("Prepare internal review once documents arrive");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldFallbackToSingleTask_WhenNoStructuredLinesExist()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        CreateProposalDto? firstProposal = null;
        CreateProposalDto? secondProposal = null;
        var invocationCount = 0;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock
            .Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) =>
            {
                if (invocationCount == 0)
                {
                    firstProposal = dto;
                }
                else
                {
                    secondProposal = dto;
                }

                invocationCount++;
            })
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "Need to clarify deployment checklist and prepare release notes for Friday.");

        var firstResult = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);
        var secondResult = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        firstProposal.Should().NotBeNull();
        secondProposal.Should().NotBeNull();
        firstProposal!.Operations.Should().ContainSingle();
        secondProposal!.Operations.Should().ContainSingle();
        firstProposal.Operations![0].IdempotencyKey.Should().Be(secondProposal.Operations![0].IdempotencyKey);
        firstProposal.Operations[0].TargetId.Should().Be(secondProposal.Operations[0].TargetId);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReturnValidationError_WhenBoardIdIsMissing()
    {
        var result = await _service.CreateProposalFromCaptureAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] task"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("BoardId is required");
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReturnNotFound_WhenBoardHasNoColumns()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync(new Board("Capture board", ownerId: userId));
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(Array.Empty<Column>());

        var result = await _service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] task"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("No columns found in board");
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReturnForbidden_WhenPermissionValidationFails()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _policyEngineMock.Setup(p => p.ValidatePermissionsAsync(
                userId,
                boardId,
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                default))
            .ReturnsAsync(Result.Failure(ErrorCodes.Forbidden, "You do not have permission to access this board"));

        var result = await _service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] restricted task"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReturnFailure_WhenProposalCreationFails()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock
            .Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, "creation failed"));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "- [ ] proposal creation failure path");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldPropagateCancellation_WhenTokenIsAlreadyCancelled()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { column });
        _proposalServiceMock
            .Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] cancelled before any work"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // An already-cancelled request must fail fast before creating any proposal.
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldSplitDashSeparatedText_IntoIndividualTasks()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "ACME onboarding - request ID documents - send engagement letter - schedule call");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(3);
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().HaveCount(3);
        createdProposal.Operations![0].Parameters.Should().Contain("request ID documents");
        createdProposal.Operations[0].Parameters.Should().Contain("ACME onboarding");
        createdProposal.Operations[1].Parameters.Should().Contain("send engagement letter");
        createdProposal.Operations[2].Parameters.Should().Contain("schedule call");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldSplitSemicolonSeparatedText_IntoIndividualTasks()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "Friday release prep; update changelog; tag version; notify stakeholders");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(4);
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().HaveCount(4);
        createdProposal.Operations![0].Parameters.Should().Contain("Friday release prep");
        createdProposal.Operations[1].Parameters.Should().Contain("update changelog");
        createdProposal.Operations[2].Parameters.Should().Contain("tag version");
        createdProposal.Operations[3].Parameters.Should().Contain("notify stakeholders");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldCreateSingleCard_ForPlainSentenceWithoutDelimiters()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "Remember to check the deployment logs after lunch");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(1);
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().ContainSingle();
        createdProposal.Operations![0].Parameters.Should().Contain("Remember to check the deployment logs after lunch");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldIncludeContextHintInEvidence_ForDashSeparatedText()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "ACME Ltd - request documents - send letter - schedule call");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(3);
        createdProposal.Should().NotBeNull();
        // Title should be just the task, evidence should include context
        createdProposal!.Operations![0].Parameters.Should().Contain("\"title\":\"request documents\"");
        createdProposal.Operations[0].Parameters.Should().Contain("ACME Ltd: request documents");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldFallToSingleCard_WhenOnlyTwoDashSegments()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "fix the deployment bug - deploy to staging");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        // Two dash segments should not trigger context-hint splitting
        // Falls through to single-sentence fallback
        result.Value.OperationCount.Should().Be(1);
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldNotSplitDashes_WhenStructuredBulletLinesExist()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        // Structured bullets take priority even if text also contains dashes
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "- Fix the deployment - it is broken\n- Update docs");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(2);
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().HaveCount(2);
        createdProposal.Operations![0].Parameters.Should().Contain("Fix the deployment");
        createdProposal.Operations[1].Parameters.Should().Contain("Update docs");
    }

    #region LLM transcript triage strategy (REVIVAL-08 M1)

    private CaptureTriageService BuildServiceWithExtractor(Mock<ILlmCaptureTriageExtractor> extractorMock)
    {
        return new CaptureTriageService(
            _unitOfWorkMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            extractorMock.Object);
    }

    private void SetupBoardAndProposalCreation(
        Guid userId, Guid boardId, Guid captureId, Action<CreateProposalDto>? onCreate = null)
    {
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => onCreate?.Invoke(dto))
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));
    }

    private static CapturePayloadV1 TranscriptPayload(string text = "Alice: I'll send the report.\nBob: Sounds good.")
        => new(CaptureRequestContract.CurrentSchemaVersion, CaptureSource.TranscriptPaste, text);

    private static LlmCaptureTriageExtraction SuccessfulExtraction(params (string Title, string Evidence)[] tasks)
    {
        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionLlmV1,
            tasks.Select(t => new CaptureTriageTaskV1(t.Title, t.Evidence)).ToList());
        return new LlmCaptureTriageExtraction(
            LlmCaptureTriageOutcome.Succeeded,
            output,
            Provider: "OpenAI",
            Model: "gpt-4o-mini");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRejectRevokedBoardAccessBeforeLlmExtraction()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        _policyEngineMock.Setup(p => p.ValidateBoardAccessAsync(userId, boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ErrorCodes.Forbidden, "User does not have access to board"));

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>(MockBehavior.Strict);
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _policyEngineMock.Verify(
            p => p.ValidateBoardAccessAsync(userId, boardId, It.IsAny<CancellationToken>()),
            Times.Once);
        extractorMock.Verify(
            e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _boardsMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _columnsMock.Verify(r => r.GetByBoardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _policyEngineMock.Verify(
            p => p.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _proposalServiceMock.Verify(
            s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldKeepFinalPermissionGateAfterLlmExtraction()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);
        _policyEngineMock.Setup(p => p.ValidatePermissionsAsync(
                userId,
                boardId,
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ErrorCodes.Forbidden, "Board access was revoked during extraction"));

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(userId, boardId, It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulExtraction(("Send the report", "Alice: send the report.")));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _policyEngineMock.Verify(
            p => p.ValidateBoardAccessAsync(userId, boardId, It.IsAny<CancellationToken>()),
            Times.Once);
        _policyEngineMock.Verify(
            p => p.ValidatePermissionsAsync(
                userId,
                boardId,
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        extractorMock.Verify(
            e => e.ExtractAsync(userId, boardId, It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _proposalServiceMock.Verify(
            s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldUseLlmOutputAndRecordRealProviderProvenance_WhenExtractionSucceeds()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        CreateProposalDto? createdProposal = null;
        SetupBoardAndProposalCreation(userId, boardId, captureId, dto => createdProposal = dto);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(userId, boardId, It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulExtraction(
                ("Send the quarterly report", "Alice: I will send the report."),
                ("Review the deployment plan", "Bob: I will review the deployment plan tomorrow.")));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(2);
        result.Value.Provider.Should().Be("OpenAI");
        result.Value.Model.Should().Be("gpt-4o-mini");
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV1);
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().HaveCount(2);
        createdProposal.Operations![0].Parameters.Should().Contain("Send the quarterly report");
        // Evidence rides in the card description so the review rail can show the verbatim quote.
        createdProposal.Operations[0].Parameters.Should().Contain("Alice: I will send the report.");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldFallBackToDeterministicExtractor_WhenLlmLegFails()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.ProviderDegraded, Detail: "circuit open"));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("- [ ] Follow up with Alice\n- [ ] Ship the fix"));

        // Degraded LLM must never fail the capture: the deterministic extractor runs and its
        // provenance names the extractor, not the LLM that did not produce the output (#1273).
        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(2);
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        result.Value.Model.Should().Be(CaptureTriageService.TriageModelName);
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionV1);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldFallBackToDeterministicExtractor_WhenExtractorThrows()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected provider bug"));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("- [ ] Survive extractor bugs"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        result.Value.Model.Should().Be(CaptureTriageService.TriageModelName);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReturnTriagedWithoutProposal_WhenLlmReportsNoActionableItems()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.EmptyExtraction,
                Provider: "OpenAI",
                Model: "gpt-4o-mini"));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("Just chit-chat, nothing actionable."));

        // A deliberate zero-item verdict must not degrade to the deterministic extractor (whose
        // whole-text fallback would fabricate a junk card) NOR surface as a failure (a correct
        // extraction is a successful triage, not an error the user should retry). It is the
        // "triaged, nothing to propose" success shape: no proposal, provenance naming the LLM.
        result.IsSuccess.Should().BeTrue();
        result.Value.ProposalId.Should().BeNull();
        result.Value.OperationCount.Should().Be(0);
        result.Value.Provider.Should().Be("OpenAI");
        result.Value.Model.Should().Be("gpt-4o-mini");
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV1);
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldNeverInvokeExtractor_ForNonTranscriptSources()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>(MockBehavior.Strict);
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Plain typed capture"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        extractorMock.Verify(
            e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldSkipLlmCall_WhenProposalAlreadyExists()
    {
        // A crashed prior attempt already committed the proposal; the retry must not burn a second
        // LLM call for output that would be discarded by the reuse branch.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        var existingProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Capture triage",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            captureId.ToString());
        existingProposal.AddOperation(new AutomationProposalOperation(
            existingProposal.Id, 0, "create", "card", "{\"title\":\"existing\"}", "existing-op-1"));

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProposal);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>(MockBehavior.Strict);
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeTrue();
        result.Value.ProposalId.Should().Be(existingProposal.Id);
        extractorMock.Verify(
            e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _policyEngineMock.Verify(
            p => p.ValidateBoardAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _policyEngineMock.Verify(
            p => p.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _boardsMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _columnsMock.Verify(r => r.GetByBoardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReportUnknownReuseProvenance_WhenLlmCouldHaveAuthoredExistingProposal()
    {
        // Crash window: a prior run committed the proposal but died before stamping the payload.
        // On retry the author is unknowable (either engine could have produced it), so naming a
        // concrete engine would risk false provenance (#1273) — "unknown" is the honest value.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        var existingProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Capture triage",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            captureId.ToString());
        existingProposal.AddOperation(new AutomationProposalOperation(
            existingProposal.Id, 0, "create", "card", "{\"title\":\"existing\"}", "existing-op-1"));

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProposal);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>(MockBehavior.Strict);
        var service = BuildServiceWithExtractor(extractorMock);

        // Payload provenance was never stamped (no provider/model on it).
        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be(CaptureTriageService.UnknownProvenanceValue);
        result.Value.Model.Should().Be(CaptureTriageService.UnknownProvenanceValue);
        result.Value.PromptVersion.Should().Be(CaptureTriageService.UnknownProvenanceValue);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldPreserveStampedReuseProvenance_WhenPayloadCarriesIt()
    {
        // If the payload already carries the authoring run's stamp, the reuse branch must echo the
        // author's own record instead of the current run's engine.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        var existingProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Capture triage",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            captureId.ToString());
        existingProposal.AddOperation(new AutomationProposalOperation(
            existingProposal.Id, 0, "create", "card", "{\"title\":\"existing\"}", "existing-op-1"));

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProposal);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>(MockBehavior.Strict);
        var service = BuildServiceWithExtractor(extractorMock);

        var stampedPayload = CaptureRequestContract.WithProvenance(
            TranscriptPayload(),
            captureId,
            promptVersion: CaptureTriageOutputContract.PromptVersionLlmV1,
            provider: "OpenAI",
            model: "gpt-4o-mini");

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, stampedPayload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be("OpenAI");
        result.Value.Model.Should().Be("gpt-4o-mini");
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV1);
    }

    #endregion

    private static ProposalDto BuildProposalDto(Guid userId, Guid boardId, Guid captureId)
    {
        return new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Queue,
            captureId.ToString(),
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "Capture triage",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            null,
            null,
            null,
            null,
            Guid.NewGuid().ToString(),
            new List<ProposalOperationDto>());
    }
}
