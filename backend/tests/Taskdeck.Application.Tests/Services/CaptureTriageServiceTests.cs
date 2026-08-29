using FluentAssertions;
using Moq;
using System.Text.Json;
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
    private readonly Mock<IProposalRevisionRepository> _proposalRevisionsMock;
    private readonly Mock<IProposalRevisionService> _proposalRevisionServiceMock;
    private readonly CaptureTriageService _service;

    public CaptureTriageServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardsMock = new Mock<IBoardRepository>();
        _columnsMock = new Mock<IColumnRepository>();
        _automationProposalsMock = new Mock<IAutomationProposalRepository>();
        _proposalServiceMock = new Mock<IAutomationProposalService>();
        _policyEngineMock = new Mock<IAutomationPolicyEngine>();
        _proposalRevisionsMock = new Mock<IProposalRevisionRepository>();
        _proposalRevisionServiceMock = new Mock<IProposalRevisionService>();

        _unitOfWorkMock.SetupGet(u => u.Boards).Returns(_boardsMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Columns).Returns(_columnsMock.Object);
        _unitOfWorkMock.SetupGet(u => u.AutomationProposals).Returns(_automationProposalsMock.Object);
        _unitOfWorkMock.SetupGet(u => u.ProposalRevisions).Returns(_proposalRevisionsMock.Object);
        _proposalRevisionsMock
            .Setup(r => r.GetLatestByProposalIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalRevision?)null);
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(
                It.IsAny<ProposalSourceType>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);
        _policyEngineMock.Setup(p => p.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        // Permissive on the bar here so the default fixture stays neutral; the tests that care
        // pin BoardAccessBar.Write explicitly (the worker lane is a mutation lane, #1836).
        _policyEngineMock.Setup(p => p.ValidateBoardAccessAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<BoardAccessBar>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _policyEngineMock.Setup(p => p.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<BoardAccessBar>(),
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
    public async Task CreateProposalFromCaptureAsync_ShouldReconcileInterruptedCaptureMetadataIntoOneRevision()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = CreateExistingCaptureProposal(userId, boardId, captureId,
            "{\"title\":\"existing\",\"description\":\"keep me\",\"columnId\":\"column-1\",\"dueDate\":\"2026-08-28T00:00:00+00:00\",\"labels\":[\"old\"]}");
        ProposalRevision? latestRevision = null;
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRevisionsMock
            .Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => latestRevision);

        CreateProposalRevisionDto? savedRevision = null;
        _proposalRevisionServiceMock
            .Setup(service => service.CreateRevisionWithPendingCommitGuardAsync(It.IsAny<CreateProposalRevisionDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProposalRevisionDto, CancellationToken>((dto, _) =>
            {
                savedRevision = dto;
                latestRevision = new ProposalRevision(proposal.Id, 1, userId, dto.RevisedPayload, dto.Reason);
            })
            .ReturnsAsync(() => Result.Success(new ProposalRevisionDto(
                Guid.NewGuid(), proposal.Id, 1, userId, latestRevision!.RevisedPayload,
                DateTimeOffset.UtcNow, latestRevision.Reason, DateTimeOffset.UtcNow)));

        var service = BuildServiceWithRevisionService();
        var correctedPayload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "- [ ] existing",
            DueDate: new DateOnly(2026, 8, 29),
            Labels: ["Sales, EMEA"]);

        var first = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, correctedPayload);
        var second = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, correctedPayload);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.ProposalId.Should().Be(proposal.Id);
        second.Value.ProposalId.Should().Be(proposal.Id);
        savedRevision.Should().NotBeNull();
        _proposalRevisionServiceMock.Verify(
            revisionService => revisionService.CreateRevisionWithPendingCommitGuardAsync(It.IsAny<CreateProposalRevisionDto>(), It.IsAny<CancellationToken>()),
            Times.Once);

        using var revisionDocument = JsonDocument.Parse(savedRevision!.RevisedPayload);
        var operation = revisionDocument.RootElement.GetProperty("operations").EnumerateArray().Single();
        operation.GetProperty("id").GetGuid().Should().Be(proposal.Operations.Single().Id);
        operation.GetProperty("idempotencyKey").GetString().Should().Be("existing-op-1");
        using var parametersDocument = JsonDocument.Parse(operation.GetProperty("parameters").GetString()!);
        parametersDocument.RootElement.GetProperty("title").GetString().Should().Be("existing");
        parametersDocument.RootElement.GetProperty("description").GetString().Should().Be("keep me");
        parametersDocument.RootElement.GetProperty("columnId").GetString().Should().Be("column-1");
        parametersDocument.RootElement.GetProperty("dueDate").GetDateTimeOffset().Should().Be(
            new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero));
        parametersDocument.RootElement.GetProperty("labels").EnumerateArray().Select(label => label.GetString())
            .Should().Equal("Sales, EMEA");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldClearDueDateAndLabelsOnlyWhenMetadataWasExplicitlyReplaced()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = CreateExistingCaptureProposal(userId, boardId, captureId,
            "{\"title\":\"existing\",\"dueDate\":\"2026-08-28T00:00:00+00:00\",\"labels\":[\"inferred\"]}");
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        CreateProposalRevisionDto? savedRevision = null;
        _proposalRevisionServiceMock
            .Setup(service => service.CreateRevisionWithPendingCommitGuardAsync(It.IsAny<CreateProposalRevisionDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProposalRevisionDto, CancellationToken>((dto, _) => savedRevision = dto)
            .ReturnsAsync(Result.Success(new ProposalRevisionDto(
                Guid.NewGuid(), proposal.Id, 1, userId, "{}", DateTimeOffset.UtcNow, "metadata", DateTimeOffset.UtcNow)));

        var service = BuildServiceWithRevisionService();
        var untouched = await service.CreateProposalFromCaptureAsync(
            captureId, userId, boardId,
            new CapturePayloadV1(CaptureRequestContract.CurrentSchemaVersion, CaptureSource.Typed, "- [ ] existing"));
        untouched.IsSuccess.Should().BeTrue();
        _proposalRevisionServiceMock.Verify(
            revisionService => revisionService.CreateRevisionWithPendingCommitGuardAsync(It.IsAny<CreateProposalRevisionDto>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var unchangedExplicitReplacement = await service.CreateProposalFromCaptureAsync(
            captureId, userId, boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] existing",
                DueDate: new DateOnly(2026, 8, 28),
                Labels: ["inferred"]));
        unchangedExplicitReplacement.IsSuccess.Should().BeTrue();
        _proposalRevisionServiceMock.Verify(
            revisionService => revisionService.CreateRevisionWithPendingCommitGuardAsync(It.IsAny<CreateProposalRevisionDto>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var cleared = await service.CreateProposalFromCaptureAsync(
            captureId, userId, boardId,
            new CapturePayloadV1(CaptureRequestContract.CurrentSchemaVersion, CaptureSource.Typed, "- [ ] existing", Labels: []));
        cleared.IsSuccess.Should().BeTrue();
        savedRevision.Should().NotBeNull();
        using var revisionDocument = JsonDocument.Parse(savedRevision!.RevisedPayload);
        using var parametersDocument = JsonDocument.Parse(
            revisionDocument.RootElement.GetProperty("operations")[0].GetProperty("parameters").GetString()!);
        parametersDocument.RootElement.TryGetProperty("dueDate", out _).Should().BeFalse();
        parametersDocument.RootElement.GetProperty("labels").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldFailClosedWhenCorrectingDecidedProposal()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = CreateExistingCaptureProposal(userId, boardId, captureId,
            "{\"title\":\"existing\",\"labels\":[\"old\"]}");
        proposal.Approve(userId);
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await BuildServiceWithRevisionService().CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(CaptureRequestContract.CurrentSchemaVersion, CaptureSource.Typed, "- [ ] existing", Labels: ["new"]));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        _proposalRevisionServiceMock.Verify(
            revisionService => revisionService.CreateRevisionWithPendingCommitGuardAsync(It.IsAny<CreateProposalRevisionDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(ErrorCodes.Forbidden)]
    [InlineData(ErrorCodes.NotFound)]
    public async Task CreateProposalFromCaptureAsync_ShouldRequireCurrentValidationBeforeSavingRecoveryRevision(string errorCode)
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = CreateExistingCaptureProposal(userId, boardId, captureId,
            "{\"title\":\"existing\",\"labels\":[\"old\"]}");
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _policyEngineMock
            .Setup(policy => policy.ValidatePermissionsAsync(
                userId,
                boardId,
                It.Is<IEnumerable<ProposalOperationDto>>(operations => operations.Single().Parameters.Contains("new")),
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(errorCode, "Current recovery validation failed"));

        var result = await BuildServiceWithRevisionService().CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(CaptureRequestContract.CurrentSchemaVersion, CaptureSource.Typed, "- [ ] existing", Labels: ["new"]));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(errorCode);
        _proposalRevisionServiceMock.Verify(
            revisionService => revisionService.CreateRevisionWithPendingCommitGuardAsync(
                It.IsAny<CreateProposalRevisionDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldTreatMissingAndEmptyLabelsAsEqualForDecidedProposal()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = CreateExistingCaptureProposal(userId, boardId, captureId, "{\"title\":\"existing\"}");
        proposal.Approve(userId);
        _automationProposalsMock
            .Setup(r => r.GetBySourceReferenceAsync(ProposalSourceType.Queue, captureId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await BuildServiceWithRevisionService().CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(CaptureRequestContract.CurrentSchemaVersion, CaptureSource.Typed, "- [ ] existing", Labels: []));

        result.IsSuccess.Should().BeTrue();
        result.Value.ProposalId.Should().Be(proposal.Id);
        _policyEngineMock.Verify(
            policy => policy.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<BoardAccessBar>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _proposalRevisionServiceMock.Verify(
            revisionService => revisionService.CreateRevisionWithPendingCommitGuardAsync(
                It.IsAny<CreateProposalRevisionDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
    public async Task CreateProposalFromCaptureAsync_ShouldCarryExplicitDueDateAndLabelsIntoCreateOperation()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        CreateProposalDto? createdProposal = null;
        SetupBoardAndProposalCreation(userId, boardId, captureId, dto => createdProposal = dto);

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "- [ ] Buy milk",
            DueDate: new DateOnly(2026, 8, 23),
            Labels: ["shopping"]);

        var result = await _service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        createdProposal.Should().NotBeNull();
        using var parameters = System.Text.Json.JsonDocument.Parse(createdProposal!.Operations![0].Parameters);
        parameters.RootElement.GetProperty("dueDate").GetDateTimeOffset().Date.Should().Be(new DateTime(2026, 8, 23));
        parameters.RootElement.GetProperty("labels").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal("shopping");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldUseExtractorDueDateHintWhenCaptureHasNoExplicitDueDate()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        CreateProposalDto? createdProposal = null;
        SetupBoardAndProposalCreation(userId, boardId, captureId, dto => createdProposal = dto);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(extractor => extractor.ExtractAsync(userId, boardId, It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.Succeeded,
                new CaptureTriageOutputV2(
                    CaptureTriageOutputContract.SchemaVersionV2,
                    CaptureTriageOutputContract.PromptVersionLlmV2,
                    [new CaptureTriageTaskV2("Send report", "action", null, "2026-08-24", 0.9m, "Send report by Friday")]),
                "OpenAI",
                "gpt-4o-mini"));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeTrue();
        createdProposal.Should().NotBeNull();
        using var parameters = System.Text.Json.JsonDocument.Parse(createdProposal!.Operations![0].Parameters);
        parameters.RootElement.GetProperty("dueDate").GetDateTimeOffset().Date.Should().Be(new DateTime(2026, 8, 24));
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldPreferExplicitDueDateOverExtractorHint()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        CreateProposalDto? createdProposal = null;
        SetupBoardAndProposalCreation(userId, boardId, captureId, dto => createdProposal = dto);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(extractor => extractor.ExtractAsync(userId, boardId, It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.Succeeded,
                new CaptureTriageOutputV2(
                    CaptureTriageOutputContract.SchemaVersionV2,
                    CaptureTriageOutputContract.PromptVersionLlmV2,
                    [new CaptureTriageTaskV2("Send report", "action", null, "2026-08-24", 0.9m, "Send report by Friday")]),
                "OpenAI",
                "gpt-4o-mini"));
        var service = BuildServiceWithExtractor(extractorMock);
        var payload = TranscriptPayload() with { DueDate = new DateOnly(2026, 8, 23) };

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, payload);

        result.IsSuccess.Should().BeTrue();
        createdProposal.Should().NotBeNull();
        using var parameters = System.Text.Json.JsonDocument.Parse(createdProposal!.Operations![0].Parameters);
        parameters.RootElement.GetProperty("dueDate").GetDateTimeOffset().Date.Should().Be(new DateTime(2026, 8, 23));
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
        CreateProposalDto? createdProposal = null;

        _boardsMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnsMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .Callback<CreateProposalDto, CancellationToken>((dto, _) => createdProposal = dto)
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
        createdProposal.Should().NotBeNull();
        createdProposal!.ProvenanceModelId.Should().Be("capture-triage-v1");
        createdProposal.TrustedConfidence!.Source.Should().Be(ProvenanceConfidenceSource.Deterministic);
        createdProposal.TrustedConfidence.Operations.Should().OnlyContain(item => item.Value == null);
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
                BoardAccessBar.Write,
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

    #region LLM transcript triage strategy (REVIVAL-08 M3)

    private CaptureTriageService BuildServiceWithExtractor(Mock<ILlmCaptureTriageExtractor> extractorMock)
    {
        return new CaptureTriageService(
            _unitOfWorkMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            extractorMock.Object);
    }

    private CaptureTriageService BuildServiceWithRevisionService() => new(
        _unitOfWorkMock.Object,
        _proposalServiceMock.Object,
        _policyEngineMock.Object,
        proposalRevisionService: _proposalRevisionServiceMock.Object);

    private static AutomationProposal CreateExistingCaptureProposal(
        Guid userId,
        Guid boardId,
        Guid captureId,
        string parameters)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Capture triage",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            captureId.ToString());
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType: "create",
            targetType: "card",
            parameters,
            idempotencyKey: "existing-op-1",
            targetId: Guid.NewGuid().ToString()));
        return proposal;
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

    private static LlmCaptureTriageExtraction SuccessfulExtraction(params (string Title, string EvidenceQuote)[] tasks)
    {
        var output = new CaptureTriageOutputV2(
            CaptureTriageOutputContract.SchemaVersionV2,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            tasks.Select(t => new CaptureTriageTaskV2(
                t.Title,
                "action",
                AssigneeHint: null,
                DueDateHint: null,
                Confidence: 0.9m,
                EvidenceQuote: t.EvidenceQuote)).ToList());
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
        // Pins the bar the worker lane asks for: BoardAccessBar.Write (#1836). If the worker were
        // switched to the Read bar this Setup would not match, the mock would return a null Result
        // and the test would fail — the assertion is not merely "some gate ran".
        _policyEngineMock.Setup(p => p.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Write, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ErrorCodes.Forbidden, "User does not have write access to board"));

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>(MockBehavior.Strict);
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _policyEngineMock.Verify(
            p => p.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Write, It.IsAny<CancellationToken>()),
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
                It.IsAny<BoardAccessBar>(),
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
                BoardAccessBar.Write,
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
            p => p.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Write, It.IsAny<CancellationToken>()),
            Times.Once);
        _policyEngineMock.Verify(
            p => p.ValidatePermissionsAsync(
                userId,
                boardId,
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                BoardAccessBar.Write,
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
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().HaveCount(2);
        createdProposal.Operations![0].Parameters.Should().Contain("Send the quarterly report");
        // Evidence rides in the card description so the review rail can show the verbatim quote.
        createdProposal.Operations[0].Parameters.Should().Contain("Alice: I will send the report.");
        createdProposal.ProvenanceModelId.Should().Be("gpt-4o-mini");
        createdProposal.TrustedConfidence!.Source.Should().Be(ProvenanceConfidenceSource.ModelReported);
        createdProposal.TrustedConfidence.Operations.Select(item => item.Value)
            .Should().Equal(0.9, 0.9);
    }

    [Fact]
    public async Task CreateProposalFromTranscriptAsync_PassesTranscriptIdAndAmbiguousSpansAsNull()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();
        CreateProposalDto? createdProposal = null;
        IReadOnlyList<TranscriptEvidenceLinkInput>? createdEvidence = null;
        SetupBoardAndProposalCreation(userId, boardId, captureId);
        _proposalServiceMock
            .Setup(s => s.CreateTranscriptProposalAsync(
                It.IsAny<CreateProposalDto>(),
                It.IsAny<IReadOnlyList<TranscriptEvidenceLinkInput>>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateProposalDto, IReadOnlyList<TranscriptEvidenceLinkInput>, CancellationToken>(
                (dto, evidence, _) =>
                {
                    createdProposal = dto;
                    createdEvidence = evidence;
                })
            .ReturnsAsync(Result.Success(BuildProposalDto(userId, boardId, captureId)));

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(userId, boardId, It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.Succeeded,
                new CaptureTriageOutputV2(
                    CaptureTriageOutputContract.SchemaVersionV2,
                    CaptureTriageOutputContract.PromptVersionLlmV2,
                    [new CaptureTriageTaskV2("Review item", "action", null, null, 0.9m, "repeated quote")]),
                Provider: "OpenAI",
                Model: "gpt-4o-mini",
                EvidenceSpans: [null]));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromTranscriptAsync(
            captureId,
            userId,
            boardId,
            transcriptId,
            TranscriptPayload("repeated quote"));

        result.IsSuccess.Should().BeTrue();
        createdProposal.Should().NotBeNull();
        createdEvidence.Should().ContainSingle();
        createdEvidence![0].OperationSequence.Should().Be(0);
        createdEvidence[0].TranscriptId.Should().Be(transcriptId);
        createdEvidence[0].SpanStart.Should().BeNull();
        createdEvidence[0].SpanEnd.Should().BeNull();
        createdProposal!.TrustedConfidence!.Source.Should().Be(ProvenanceConfidenceSource.ModelReported);
        createdProposal.TrustedConfidence.Operations.Should().ContainSingle().Which.Value.Should().Be(0.9);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldKeepV2MetadataOutOfExecutableOperationParameters()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        CreateProposalDto? createdProposal = null;
        SetupBoardAndProposalCreation(userId, boardId, captureId, dto => createdProposal = dto);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        var output = new CaptureTriageOutputV2(
            CaptureTriageOutputContract.SchemaVersionV2,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            [new CaptureTriageTaskV2(
                "Record the launch decision",
                "decision",
                "Alice",
                "2026-08-07",
                0.98m,
                "Alice: we decided to launch on August 7.")]);
        extractorMock
            .Setup(e => e.ExtractAsync(userId, boardId, It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.Succeeded,
                output,
                Provider: "OpenAI",
                Model: "gpt-4o-mini"));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeTrue();
        createdProposal.Should().NotBeNull();
        createdProposal!.Operations.Should().ContainSingle();
        var parameters = createdProposal.Operations[0].Parameters;
        parameters.Should().Contain("Alice: we decided to launch on August 7.");
        parameters.Should().NotContain("\"type\"");
        parameters.Should().NotContain("assigneeHint");
        parameters.Should().NotContain("dueDateHint");
        parameters.Should().NotContain("confidence");
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
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
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
            p => p.ValidateBoardAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<BoardAccessBar>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _policyEngineMock.Verify(
            p => p.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                It.IsAny<BoardAccessBar>(),
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
            promptVersion: CaptureTriageOutputContract.PromptVersionLlmV2,
            provider: "OpenAI",
            model: "gpt-4o-mini");

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, stampedPayload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be("OpenAI");
        result.Value.Model.Should().Be("gpt-4o-mini");
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
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

    // ---------------------------------------------------------------------------------------
    // #2192 — an attempted LLM triage leg that cannot deliver must never fall back silently.
    // Before this, every non-LLM outcome was recorded only in an ILogger call, so a user whose
    // provider request failed (nonexistent model / non-2xx) received a deterministic proposal
    // with no degraded message anywhere on the capture.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRecordDegradedNotice_WhenLiveProviderRequestFails()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        CreateProposalDto? createdProposal = null;
        SetupBoardAndProposalCreation(userId, boardId, captureId, dto => createdProposal = dto);

        // The exact shape a failed live request takes: OpenAiLlmProvider returns a DEGRADED result
        // (it does not throw) for a non-2xx / unknown-model response, which the extractor maps to
        // ProviderDegraded carrying the provider's reason.
        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.ProviderDegraded,
                Detail: "Live provider request failed."));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("- [ ] Follow up with Alice\n- [ ] Ship the fix"));

        result.IsSuccess.Should().BeTrue();
        result.Value.OperationCount.Should().Be(2);

        // The degradation is carried out of the service so the worker can record it on the capture.
        result.Value.DegradedNotice.Should().NotBeNull();
        result.Value.DegradedNotice.Should().StartWith(CaptureTriageService.DegradedTriageNoticePrefix);
        result.Value.DegradedNotice.Should().Contain(nameof(LlmCaptureTriageOutcome.ProviderDegraded));
        result.Value.DegradedNotice.Should().Contain("using deterministic extractor");
        result.Value.DegradedNotice.Should().Contain("Live provider request failed.");

        // ...and the proposal still names the engine that actually authored it, with no
        // model-style confidence attached to deterministic output.
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        result.Value.Model.Should().Be(CaptureTriageService.TriageModelName);
        createdProposal.Should().NotBeNull();
        createdProposal!.ProvenanceModelId.Should().Be(CaptureTriageService.TriageModelName);
        createdProposal.TrustedConfidence!.Source.Should().Be(ProvenanceConfidenceSource.Deterministic);
        createdProposal.TrustedConfidence.Operations.Should().OnlyContain(op => op.Value == null);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRecordDegradedNotice_WhenProviderDegradesWithoutDetail()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.ProviderDegraded));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("- [ ] Ship the fix"));

        result.IsSuccess.Should().BeTrue();

        // A missing provider detail must still name the outcome, not degrade to a bare or empty
        // notice: naming the outcome is the whole point of the record.
        result.Value.DegradedNotice.Should()
            .Be($"{CaptureTriageService.DegradedTriageNoticePrefix} (ProviderDegraded); using deterministic extractor");
    }

    [Theory]
    [InlineData(LlmCaptureTriageOutcome.KillSwitchActive)]
    [InlineData(LlmCaptureTriageOutcome.ProviderUnavailable)]
    [InlineData(LlmCaptureTriageOutcome.QuotaExceeded)]
    [InlineData(LlmCaptureTriageOutcome.ProviderDegraded)]
    [InlineData(LlmCaptureTriageOutcome.InvalidOutput)]
    public async Task CreateProposalFromCaptureAsync_ShouldRecordDegradedNotice_ForEveryAttemptedButUndeliveredOutcome(
        LlmCaptureTriageOutcome outcome)
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        CreateProposalDto? createdProposal = null;
        SetupBoardAndProposalCreation(userId, boardId, captureId, dto => createdProposal = dto);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(outcome, Detail: "degradation detail"));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("- [ ] Ship the fix"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DegradedNotice.Should().NotBeNull();
        result.Value.DegradedNotice.Should().Contain(outcome.ToString());
        result.Value.DegradedNotice.Should().Contain("using deterministic extractor");
        createdProposal!.TrustedConfidence!.Source.Should().Be(ProvenanceConfidenceSource.Deterministic);
    }

    [Theory]
    [InlineData(LlmCaptureTriageOutcome.Disabled)]
    [InlineData(LlmCaptureTriageOutcome.ProviderIsMock)]
    public async Task CreateProposalFromCaptureAsync_ShouldNotRecordDegradedNotice_WhenNoLiveProviderWasEverExpected(
        LlmCaptureTriageOutcome outcome)
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(outcome));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("- [ ] Ship the fix"));

        // Disabled / mock are configuration states, not failures: the deterministic extractor is
        // the intended engine and the provenance already names it. Reporting "LLM triage
        // unavailable" here would make the ordinary offline setup look broken on every capture.
        result.IsSuccess.Should().BeTrue();
        result.Value.DegradedNotice.Should().BeNull();
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRecordDegradedNotice_WhenExtractorThrows()
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
        result.Value.DegradedNotice.Should().NotBeNull();
        result.Value.DegradedNotice.Should().Contain(nameof(LlmCaptureTriageOutcome.InvalidOutput));
        result.Value.DegradedNotice.Should().Contain(CaptureTriageService.UnexpectedExtractorFailureDetail);

        // The exception's own message is arbitrary text from any layer the extractor touches and
        // this notice is published on the capture, so it must never be a pass-through. Pattern
        // redaction cannot be the guard here: it only masks shapes it already recognizes.
        result.Value.DegradedNotice.Should().NotContain("unexpected provider bug");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldNotPublishSecretsFromAnUnexpectedExtractorException()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        // A shape SensitiveDataRedactor does not recognize, which is exactly why the message is
        // dropped rather than redacted.
        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "connect failed for C:\\srv\\secrets\\prod.db (cred sk-live-abcdef1234567890)"));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("- [ ] Ship the fix"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DegradedNotice.Should().NotBeNull();
        result.Value.DegradedNotice.Should().NotContain("sk-live-abcdef1234567890");
        result.Value.DegradedNotice.Should().NotContain("C:\\srv\\secrets");
        result.Value.DegradedNotice.Should().NotContain("prod.db");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRecordDegradedNotice_WhenSucceededOutputFailsContractValidation()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        // A leg that reports Succeeded but whose payload cannot pass the contract still falls back
        // to the deterministic extractor, so it belongs to the same degradation class.
        var invalidOutput = new CaptureTriageOutputV2(
            CaptureTriageOutputContract.SchemaVersionV2,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            new List<CaptureTriageTaskV2>());
        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.Succeeded,
                invalidOutput,
                Provider: "OpenAI",
                Model: "gpt-4o-mini"));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayload("- [ ] Ship the fix"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DegradedNotice.Should().NotBeNull();
        result.Value.DegradedNotice.Should().Contain(nameof(LlmCaptureTriageOutcome.InvalidOutput));
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldNotRecordDegradedNotice_WhenTheLlmProducedTheProposal()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulExtraction(("Send the report", "Alice: I'll send the report.")));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload());

        result.IsSuccess.Should().BeTrue();
        result.Value.DegradedNotice.Should().BeNull();
        result.Value.Provider.Should().Be("OpenAI");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldNameTheDegradation_WhenTheDeterministicFallbackAlsoFindsNothing()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupBoardAndProposalCreation(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.ProviderDegraded,
                Detail: "Live provider request failed."));
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(captureId, userId, boardId, TranscriptPayload("   "));

        // The capture fails, so the notice cannot ride the success DTO. The failure message the
        // worker persists must still say the provider leg failed first, rather than blaming the
        // user's text alone.
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("did not produce actionable triage items");
        result.ErrorMessage.Should().Contain(CaptureTriageService.DegradedTriageNoticePrefix);
        result.ErrorMessage.Should().Contain(nameof(LlmCaptureTriageOutcome.ProviderDegraded));
    }

    // ---------------------------------------------------------------------------------------
    // #2192 review, P2: the proposal-reuse replay must not lose the degradation record. A
    // degraded attempt can commit its proposal and stop before the worker stamps the capture;
    // the retry then takes the reuse short circuit, and a null notice would let the worker
    // complete the capture clean — the same silent fallback, one crash window later.
    // ---------------------------------------------------------------------------------------

    /// <summary>Arranges the already-triaged short circuit for a capture.</summary>
    private AutomationProposal SetupExistingProposalForReuse(Guid userId, Guid boardId, Guid captureId)
    {
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
        return existingProposal;
    }

    private static CapturePayloadV1 TranscriptPayloadStampedWith(string? provider, string? model)
        => TranscriptPayload("- [ ] Reuse existing") with
        {
            Provenance = provider is null
                ? null
                : new CaptureProvenanceV1(
                    Guid.NewGuid(),
                    PromptVersion: CaptureTriageOutputContract.PromptVersionV1,
                    Provider: provider,
                    Model: model)
        };

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRecoverTheDegradedNotice_WhenReusingADeterministicallyAuthoredProposal()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var existingProposal = SetupExistingProposalForReuse(userId, boardId, captureId);

        // The authoring run stamped its own provenance and it names the deterministic extractor,
        // so a fallback definitely happened on the run that built this proposal.
        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayloadStampedWith(
                CaptureTriageService.TriageProviderName,
                CaptureTriageService.TriageModelName));

        result.IsSuccess.Should().BeTrue();
        result.Value.ProposalId.Should().Be(existingProposal.Id);
        result.Value.DegradedNotice.Should().NotBeNull();
        result.Value.DegradedNotice.Should().StartWith(CaptureTriageService.DegradedTriageNoticePrefix);
        result.Value.DegradedNotice.Should().Contain("using deterministic extractor");

        // The authoring run's outcome was never persisted, so the notice must not name one —
        // inventing an outcome would be the same dishonesty pointing the other way.
        result.Value.DegradedNotice.Should().NotContain(nameof(LlmCaptureTriageOutcome.ProviderDegraded));
        result.Value.DegradedNotice.Should().NotContain(nameof(LlmCaptureTriageOutcome.InvalidOutput));

        // The replay must not spend a second extraction call for output the reuse discards.
        extractorMock.Verify(
            e => e.ExtractAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CapturePayloadV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldReportEngineUncertainty_WhenReusingAProposalFromAnInterruptedRun()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupExistingProposalForReuse(userId, boardId, captureId);

        // The exact crash window: the proposal is committed, the payload was never stamped.
        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayloadStampedWith(provider: null, model: null));

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be(CaptureTriageService.UnknownProvenanceValue);

        // Either engine could have authored it, so the record states the uncertainty rather than
        // going silent (which would hide a possible fallback) or naming an engine it cannot know.
        result.Value.DegradedNotice.Should().NotBeNull();
        result.Value.DegradedNotice.Should().Contain("unknown");
        result.Value.DegradedNotice.Should().Contain("deterministic extractor");
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRecordNoNotice_WhenReusingAModelAuthoredProposal()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupExistingProposalForReuse(userId, boardId, captureId);

        var extractorMock = new Mock<ILlmCaptureTriageExtractor>();
        var service = BuildServiceWithExtractor(extractorMock);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            TranscriptPayloadStampedWith("OpenAI", "gpt-4o-mini"));

        // The author recorded a live model, so nothing degraded and nothing should be reported.
        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be("OpenAI");
        result.Value.DegradedNotice.Should().BeNull();
    }

    [Fact]
    public async Task CreateProposalFromCaptureAsync_ShouldRecordNoNotice_WhenReusingAProposalForANonTranscriptCapture()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        SetupExistingProposalForReuse(userId, boardId, captureId);

        // A typed capture never had an LLM leg, so the deterministic extractor is the expected
        // engine and reporting a degradation would be noise on every replay.
        var result = await _service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Reuse existing"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        result.Value.DegradedNotice.Should().BeNull();
    }

    [Fact]
    public void BuildDegradedTriageNotice_ShouldRedactSecretsOutOfTheDetail()
    {
        // The notice is served to the client, unlike the log line it replaces. Details come from
        // provider reasons, validation errors, and caught exception messages, so a credential that
        // reaches one must not be published on the capture.
        var notice = CaptureTriageService.BuildDegradedTriageNotice(
            LlmCaptureTriageOutcome.ProviderUnavailable,
            "request rejected: Authorization: Bearer sk-live-should-not-appear");

        notice.Should().NotBeNull();
        notice.Should().NotContain("sk-live-should-not-appear");
        notice.Should().Contain(SensitiveDataRedactor.RedactedValue);
    }

    [Fact]
    public void BuildDegradedTriageNotice_ShouldBoundAnOverlongProviderDetail()
    {
        // A provider detail is untrusted text of unbounded length; the notice is persisted into a
        // 1000-character column, so it must be bounded before it gets there.
        var notice = CaptureTriageService.BuildDegradedTriageNotice(
            LlmCaptureTriageOutcome.ProviderUnavailable,
            new string('x', 5000));

        notice.Should().NotBeNull();
        notice!.Length.Should().BeLessThan(LlmRequest.MaxErrorMessageLength);
        notice.Should().Contain(nameof(LlmCaptureTriageOutcome.ProviderUnavailable));
    }
}
