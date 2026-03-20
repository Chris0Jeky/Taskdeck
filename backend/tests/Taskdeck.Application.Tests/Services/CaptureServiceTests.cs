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
    private readonly Mock<IAutomationProposalRepository> _automationProposalRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly CaptureService _service;

    public CaptureServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _llmQueueRepositoryMock = new Mock<ILlmQueueRepository>();
        _automationProposalRepositoryMock = new Mock<IAutomationProposalRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();

        _unitOfWorkMock.SetupGet(u => u.LlmQueue).Returns(_llmQueueRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(u => u.AutomationProposals).Returns(_automationProposalRepositoryMock.Object);
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
        var parsedPayload = CaptureRequestContract.ParsePayload(persisted.Payload, allowServerAttributionFields: true);
        parsedPayload.IsSuccess.Should().BeTrue();
        parsedPayload.Value.Source.Should().Be(CaptureSource.Paste);
        parsedPayload.Value.Text.Should().Be("quick capture text");
        parsedPayload.Value.Provenance.Should().NotBeNull();
        parsedPayload.Value.Provenance!.CaptureItemId.Should().Be(persisted.Id);
        parsedPayload.Value.Provenance.RequestedByUserId.Should().Be(userId);
        parsedPayload.Value.Provenance.SourceSurface.Should().Be("capture");
        parsedPayload.Value.Provenance.BoardId.Should().Be(boardId);
        parsedPayload.Value.Provenance.CorrelationId.Should().NotBeNullOrWhiteSpace();
        result.Value.RawText.Should().Be("quick capture text");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnValidationError_WhenSourceIsInvalid()
    {
        var userId = Guid.NewGuid();
        var user = new User("capture-user", "capture-user@example.com", "hash");
        const string sensitiveSource = "Authorization: Bearer capture-secret";
        var dto = new CreateCaptureItemDto(null, "quick capture text", sensitiveSource);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);

        var result = await _service.CreateAsync(userId, dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Invalid capture source value");
        result.ErrorMessage.Should().NotContain(sensitiveSource);
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
    public async Task ListAsync_ShouldApplyDefaultLimit_WhenLimitIsZero()
    {
        var userId = Guid.NewGuid();
        var items = Enumerable.Range(0, 60)
            .Select(i => new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, $"capture payload {i}"))
            .ToList();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(items);

        var result = await _service.ListAsync(
            userId,
            new CaptureListFilterDto(Limit: 0));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(50);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnProposalCreatedStatus_WhenCaptureHasLinkedProposalProvenance()
    {
        var userId = Guid.NewGuid();
        var captureRequest = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        "captured text"),
                    captureItemId: Guid.NewGuid(),
                    triageRunId: Guid.NewGuid(),
                    proposalId: Guid.NewGuid())));
        captureRequest.MarkAsProcessing();
        captureRequest.MarkAsCompleted();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(new[] { captureRequest });

        var result = await _service.ListAsync(userId, new CaptureListFilterDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Status.Should().Be(CaptureStatus.ProposalCreated);
    }

    [Fact]
    public async Task ListAsync_ShouldNotReturnProposalCreatedStatus_WhenProvenanceProposalIdIsEmpty()
    {
        var userId = Guid.NewGuid();
        var captureRequest = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        "captured text"),
                    captureItemId: Guid.NewGuid(),
                    triageRunId: Guid.NewGuid(),
                    proposalId: Guid.Empty)));
        captureRequest.MarkAsProcessing();
        captureRequest.MarkAsCompleted();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(new[] { captureRequest });

        var result = await _service.ListAsync(userId, new CaptureListFilterDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Status.Should().NotBe(CaptureStatus.ProposalCreated);
        result.Value[0].Status.Should().Be(CaptureStatus.Triaged);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnConvertedStatus_WhenCaptureHasPersistedConversionProvenance()
    {
        var userId = Guid.NewGuid();
        var captureRequest = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        "captured text"),
                    captureItemId: Guid.NewGuid(),
                    triageRunId: Guid.NewGuid(),
                    proposalId: Guid.NewGuid(),
                    convertedAt: DateTimeOffset.UtcNow)));
        captureRequest.MarkAsProcessing();
        captureRequest.MarkAsCompleted();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(new[] { captureRequest });

        var result = await _service.ListAsync(userId, new CaptureListFilterDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Status.Should().Be(CaptureStatus.Converted);
    }

    [Fact]
    public async Task ListAsync_ShouldBackfillConvertedStatus_WhenLinkedProposalIsAlreadyApplied()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureRequest = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(
                    CaptureRequestContract.CurrentSchemaVersion,
                    CaptureSource.Typed,
                    "captured text")));
        captureRequest.MarkAsProcessing();
        captureRequest.MarkAsCompleted();

        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Applied capture proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            captureRequest.Id.ToString());
        proposal.Approve(userId);
        proposal.MarkAsApplied();
        captureRequest.UpdatePayload(CaptureRequestContract.SerializePayload(
            CaptureRequestContract.WithProvenance(
                new CapturePayloadV1(
                    CaptureRequestContract.CurrentSchemaVersion,
                    CaptureSource.Typed,
                    "captured text"),
                captureItemId: Guid.NewGuid(),
                triageRunId: Guid.NewGuid(),
                proposalId: proposal.Id)));

        _llmQueueRepositoryMock
            .Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(new[] { captureRequest });
        _automationProposalRepositoryMock
            .Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _service.ListAsync(userId, new CaptureListFilterDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Status.Should().Be(CaptureStatus.Converted);
        captureRequest.BoardId.Should().Be(boardId);
        var payload = CaptureRequestContract.ParsePayload(captureRequest.Payload, allowServerAttributionFields: true);
        payload.IsSuccess.Should().BeTrue();
        payload.Value.Provenance.Should().NotBeNull();
        payload.Value.Provenance!.ConvertedAt.Should().NotBeNull();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldRejectAlreadyAppliedCapture_WhenConvertedProvenanceIsBackfilledLazily()
    {
        var userId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var item = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        "captured text"),
                    captureItemId: Guid.NewGuid(),
                    triageRunId: Guid.NewGuid(),
                    proposalId: proposalId)));
        item.MarkAsProcessing();
        item.MarkAsCompleted();

        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Applied capture proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            null,
            item.Id.ToString());
        proposal.Approve(userId);
        proposal.MarkAsApplied();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _automationProposalRepositoryMock
            .Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var result = await _service.EnqueueTriageAsync(userId, item.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain(CaptureStatus.Converted.ToString());
        var payload = CaptureRequestContract.ParsePayload(item.Payload, allowServerAttributionFields: true);
        payload.IsSuccess.Should().BeTrue();
        payload.Value.Provenance.Should().NotBeNull();
        payload.Value.Provenance!.ConvertedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ListAsync_ShouldReturnValidationError_WhenLimitIsNegative()
    {
        var userId = Guid.NewGuid();

        var result = await _service.ListAsync(
            userId,
            new CaptureListFilterDto(Limit: -1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("negative");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldIncludeProvenance_WhenCapturePayloadContainsLinkedProposal()
    {
        var userId = Guid.NewGuid();
        var captureItemId = Guid.NewGuid();
        var triageRunId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var item = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        "capture payload"),
                    captureItemId,
                    triageRunId,
                    proposalId,
                    "triage.v1",
                    "OpenAI",
                    "gpt-4o-mini")));

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.GetByIdAsync(userId, item.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provenance.Should().NotBeNull();
        result.Value.Provenance!.CaptureItemId.Should().Be(captureItemId);
        result.Value.Provenance.TriageRunId.Should().Be(triageRunId);
        result.Value.Provenance.ProposalId.Should().Be(proposalId);
        result.Value.Provenance.PromptVersion.Should().Be("triage.v1");
        result.Value.Provenance.Provider.Should().Be("OpenAI");
        result.Value.Provenance.Model.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldBackfillConvertedProvenance_WhenLinkedProposalIsAlreadyApplied()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(
                    CaptureRequestContract.CurrentSchemaVersion,
                    CaptureSource.Typed,
                    "capture payload")));
        item.MarkAsProcessing();
        item.MarkAsCompleted();

        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Applied capture proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            item.Id.ToString());
        proposal.Approve(userId);
        proposal.MarkAsApplied();
        item.UpdatePayload(CaptureRequestContract.SerializePayload(
            CaptureRequestContract.WithProvenance(
                new CapturePayloadV1(
                    CaptureRequestContract.CurrentSchemaVersion,
                    CaptureSource.Typed,
                    "capture payload"),
                captureItemId: Guid.NewGuid(),
                triageRunId: Guid.NewGuid(),
                proposalId: proposal.Id)));

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _automationProposalRepositoryMock
            .Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _service.GetByIdAsync(userId, item.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CaptureStatus.Converted);
        result.Value.BoardId.Should().Be(boardId);
        result.Value.Provenance.Should().NotBeNull();
        result.Value.Provenance!.ConvertedAt.Should().NotBeNull();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
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

    [Fact]
    public async Task EnqueueTriageAsync_ShouldTransitionNewCaptureToTriaging()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.EnqueueTriageAsync(userId, item.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CaptureStatus.Triaging);
        result.Value.AlreadyTriaging.Should().BeFalse();
        item.Status.Should().Be(RequestStatus.Processing);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldBeIdempotent_WhenItemIsAlreadyTriaging()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");
        item.MarkAsProcessing();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.EnqueueTriageAsync(userId, item.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CaptureStatus.Triaging);
        result.Value.AlreadyTriaging.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldReturnForbidden_WhenCaptureBelongsToDifferentUser()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var item = new LlmRequest(ownerId, CaptureRequestContract.RequestTypeV1, "capture payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.EnqueueTriageAsync(callerId, item.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldReturnConflict_WhenItemIsIgnored()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");
        item.Cancel();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.EnqueueTriageAsync(userId, item.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("cannot transition");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldReturnConflict_WhenItemIsConverted()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        "capture payload"),
                    captureItemId: Guid.NewGuid(),
                    triageRunId: Guid.NewGuid(),
                    proposalId: Guid.NewGuid(),
                    convertedAt: DateTimeOffset.UtcNow)));
        item.MarkAsProcessing();
        item.MarkAsCompleted();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.EnqueueTriageAsync(userId, item.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain(CaptureStatus.Converted.ToString());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }
}
