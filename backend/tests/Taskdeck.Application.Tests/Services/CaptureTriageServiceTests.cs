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
    private readonly Mock<ILlmProvider> _llmProviderMock;
    private readonly CaptureTriageService _service;

    public CaptureTriageServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardsMock = new Mock<IBoardRepository>();
        _columnsMock = new Mock<IColumnRepository>();
        _automationProposalsMock = new Mock<IAutomationProposalRepository>();
        _proposalServiceMock = new Mock<IAutomationProposalService>();
        _policyEngineMock = new Mock<IAutomationPolicyEngine>();
        _llmProviderMock = new Mock<ILlmProvider>();

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
        _policyEngineMock.Setup(p => p.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _llmProviderMock.Setup(p => p.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmHealthStatus(true, "Mock", Model: "mock-default"));

        _service = new CaptureTriageService(
            _unitOfWorkMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _llmProviderMock.Object);
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
        result.Value.Provider.Should().Be("Mock");
        result.Value.Model.Should().Be("mock-default");
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
        _llmProviderMock.Verify(p => p.GetHealthAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldUseUnknownProviderMetadata_WhenProviderHealthFails()
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
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));
        _llmProviderMock.Setup(p => p.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("health unavailable"));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "- [ ] provider metadata fallback");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be("unknown");
        result.Value.Model.Should().Be("unknown");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldClampProviderMetadataToContractLimits()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var board = new Board("Capture board", ownerId: userId);
        var column = new Column(boardId, "Inbox", 0);
        var longProvider = new string('p', CaptureRequestContract.MaxProviderLength + 10);
        var longModel = new string('m', CaptureRequestContract.MaxModelLength + 10);

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock
            .Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));
        _llmProviderMock.Setup(p => p.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmHealthStatus(true, longProvider, Model: longModel));

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "- [ ] metadata clamp");

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().HaveLength(CaptureRequestContract.MaxProviderLength);
        result.Value.Model.Should().HaveLength(CaptureRequestContract.MaxModelLength);
        result.Value.Provider.Should().Be(longProvider[..CaptureRequestContract.MaxProviderLength]);
        result.Value.Model.Should().Be(longModel[..CaptureRequestContract.MaxModelLength]);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldNotResolveProviderMetadata_WhenProposalCreationFails()
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
        _llmProviderMock.Verify(p => p.GetHealthAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldPropagateCancellation_WhenMetadataLookupTokenIsCancelled()
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
                "- [ ] cancelled metadata lookup"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _llmProviderMock.Verify(p => p.GetHealthAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

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
