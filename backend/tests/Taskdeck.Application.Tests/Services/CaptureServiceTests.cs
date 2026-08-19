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

    /// <summary>
    /// Models the real <see cref="ILlmQueueRepository.GetCapturesByUserAsync"/>: capture-only,
    /// newest-first (CreatedAt desc, then Id), paged by the requested limit/offset. ListAsync now
    /// relies on the repository for the capture filter + paging, so the mock must apply both.
    /// </summary>
    private void SetupCapturePage(Guid userId, IEnumerable<LlmRequest> requests)
    {
        var all = requests.ToList();
        _llmQueueRepositoryMock
            .Setup(r => r.GetCapturesByUserAsync(userId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, int limit, int offset, Guid? boardId, CancellationToken _) =>
                Task.FromResult<IEnumerable<LlmRequest>>(
                    all.Where(x => CaptureRequestContract.IsCaptureRequestType(x.RequestType))
                       // Mirror the repo's #1239 raw-board pre-filter: match the board or keep null-board rows.
                       .Where(x => !boardId.HasValue || x.BoardId == null || x.BoardId == boardId.Value)
                       .OrderByDescending(x => x.CreatedAt)
                       .ThenBy(x => x.Id)
                       .Skip(offset)
                       .Take(limit)
                       .ToList()));
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
        result.Value.CanEditSuggestion.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldAssignTranscriptRequestType_ForTranscriptSources()
    {
        // REVIVAL-08: transcript-source captures get the transcript request type so the transcript
        // worker lane owns them; the type is resolved server-side, never chosen by the client.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("capture-user", "capture-user@example.com", "hash");
        var dto = new CreateCaptureItemDto(boardId, "Alice: I will send the report.", "transcriptPaste");
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
        persisted!.RequestType.Should().Be(CaptureRequestContract.RequestTypeTranscriptV1);
        var parsedPayload = CaptureRequestContract.ParsePayload(persisted.Payload, allowServerAttributionFields: true);
        parsedPayload.IsSuccess.Should().BeTrue();
        parsedPayload.Value.Source.Should().Be(CaptureSource.TranscriptPaste);
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

        SetupCapturePage(userId, new[] { capturePending, captureCancelled, nonCapture });

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

        SetupCapturePage(userId, items);

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

        SetupCapturePage(userId, new[] { captureRequest });

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

        SetupCapturePage(userId, new[] { captureRequest });

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

        SetupCapturePage(userId, new[] { captureRequest });

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

        SetupCapturePage(userId, new[] { captureRequest });
        _automationProposalRepositoryMock
            .Setup(r => r.GetByIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { proposal.Id })),
                default))
            .ReturnsAsync(new[] { proposal });

        var result = await _service.ListAsync(userId, new CaptureListFilterDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Status.Should().Be(CaptureStatus.Converted);
        result.Value[0].BoardId.Should().Be(boardId);
        captureRequest.BoardId.Should().BeNull();
        var payload = CaptureRequestContract.ParsePayload(captureRequest.Payload, allowServerAttributionFields: true);
        payload.IsSuccess.Should().BeTrue();
        payload.Value.Provenance.Should().NotBeNull();
        payload.Value.Provenance!.ConvertedAt.Should().BeNull();
        _automationProposalRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ListAsync_ShouldOnlyBatchProposalLookupsForItemsScannedBeforeTheLimitIsReached()
    {
        var userId = Guid.NewGuid();
        var laterProposalId = Guid.NewGuid();
        var firstProposalId = Guid.NewGuid();

        var laterCapture = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        "later capture"),
                    captureItemId: Guid.NewGuid(),
                    triageRunId: Guid.NewGuid(),
                    proposalId: laterProposalId)));
        laterCapture.MarkAsProcessing();
        laterCapture.MarkAsCompleted();

        var firstCapture = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        "first capture"),
                    captureItemId: Guid.NewGuid(),
                    triageRunId: Guid.NewGuid(),
                    proposalId: firstProposalId)));
        firstCapture.MarkAsProcessing();
        firstCapture.MarkAsCompleted();

        SetupCapturePage(userId, new[] { laterCapture, firstCapture });
        _automationProposalRepositoryMock
            .Setup(r => r.GetByIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { firstProposalId })),
                default))
            .ReturnsAsync(Array.Empty<AutomationProposal>());

        var result = await _service.ListAsync(userId, new CaptureListFilterDto(Limit: 1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Id.Should().Be(firstCapture.Id);
        _automationProposalRepositoryMock.Verify(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _automationProposalRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task ListAsync_PagesUntilEnoughMatches_WhenFilterUnderfillsEarlyPages()
    {
        var userId = Guid.NewGuid();
        var boardB = Guid.NewGuid();

        // Newest-first; the three newest have a NULL raw board, so they pass the repo's #1239 board
        // pre-filter (null-board rows are kept for provenance) but fail the in-service effective-board
        // check (no applied proposal -> effective board null != boardB). Only the two oldest actually
        // match, so the loop must still advance through multiple limit-sized pages to collect them.
        var captures = new List<LlmRequest>
        {
            new(userId, CaptureRequestContract.RequestTypeV1, "newest"),
            new(userId, CaptureRequestContract.RequestTypeV1, "second"),
            new(userId, CaptureRequestContract.RequestTypeV1, "third"),
            new(userId, CaptureRequestContract.RequestTypeV1, "fourth", boardB),
            new(userId, CaptureRequestContract.RequestTypeV1, "oldest", boardB),
        };

        var requestedOffsets = new List<int>();
        _llmQueueRepositoryMock
            .Setup(r => r.GetCapturesByUserAsync(userId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, int limit, int offset, Guid? boardId, CancellationToken _) =>
            {
                requestedOffsets.Add(offset);
                // null-board + boardB rows all pass the board pre-filter; the service filters the null ones.
                return Task.FromResult<IEnumerable<LlmRequest>>(
                    captures.Where(x => !boardId.HasValue || x.BoardId == null || x.BoardId == boardId.Value)
                            .Skip(offset).Take(limit).ToList());
            });

        var result = await _service.ListAsync(userId, new CaptureListFilterDto(BoardId: boardB, Limit: 2));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(s => s.BoardId).Should().AllBeEquivalentTo(boardB);
        // Newest-first across the in-service page boundary; no row skipped or duplicated.
        result.Value.Select(s => s.Id).Should().Equal(captures[3].Id, captures[4].Id);
        // Offset advanced by the returned page size each iteration (0 -> 2 -> 4) until enough matches.
        requestedOffsets.Should().Equal(0, 2, 4);
    }

    [Fact]
    public async Task ListAsync_DeduplicatesRows_WhenOffsetPagingRefetchesABoundaryRow()
    {
        var userId = Guid.NewGuid();
        var boardB = Guid.NewGuid();
        // `a` has a NULL raw board: it passes the repo's #1239 board pre-filter (kept for provenance)
        // but the service filters it out (effective board null != boardB), so it pads the first page
        // without contributing a match -- the same role the old non-matching-board row played.
        var a = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "a");
        var b = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "b", boardB);
        var c = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "c", boardB);
        var d = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "d", boardB);

        // Simulate a concurrent insert between page reads: the offset-3 page re-surfaces `c`
        // (a boundary row already returned on the offset-0 page).
        _llmQueueRepositoryMock
            .Setup(r => r.GetCapturesByUserAsync(userId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, int limit, int offset, Guid? boardId, CancellationToken _) =>
            {
                IEnumerable<LlmRequest> page = offset switch
                {
                    0 => new[] { a, b, c },
                    3 => new[] { c, d },
                    _ => Array.Empty<LlmRequest>(),
                };
                return Task.FromResult(page);
            });

        var result = await _service.ListAsync(userId, new CaptureListFilterDto(BoardId: boardB, Limit: 3));

        result.IsSuccess.Should().BeTrue();
        // `c` is returned twice across pages but must appear once -- the dedup guard drops the re-surfaced row.
        result.Value.Select(s => s.Id).Should().Equal(b.Id, c.Id, d.Id);
        result.Value.Select(s => s.Id).Should().OnlyHaveUniqueItems();
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
    public async Task GetByIdAsync_ShouldHideEditCapability_WhenTranscriptIsLinked()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeTranscriptV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.TranscriptPaste, "canonical transcript")));
        item.AttachTranscript(Guid.NewGuid());

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.GetByIdAsync(userId, item.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanEditSuggestion.Should().BeFalse();
    }

    [Theory]
    [InlineData(RequestStatus.Pending, CaptureStatus.New)]
    [InlineData(RequestStatus.Failed, CaptureStatus.Failed)]
    [InlineData(RequestStatus.Completed, CaptureStatus.Triaged)]
    public async Task GetByIdAsync_ShouldAllowEditCapability_ForEligibleUnlinkedStatuses(
        RequestStatus queueStatus,
        CaptureStatus expectedCaptureStatus)
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.Typed, "capture payload")));
        if (queueStatus == RequestStatus.Failed)
        {
            item.MarkAsFailed("triage failed");
        }
        else if (queueStatus == RequestStatus.Completed)
        {
            item.MarkAsProcessing();
            item.MarkAsCompleted();
        }

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.GetByIdAsync(userId, item.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(expectedCaptureStatus);
        result.Value.CanEditSuggestion.Should().BeTrue();
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
        var boardId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload", boardId);

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));

        var result = await _service.EnqueueTriageAsync(userId, item.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CaptureStatus.Triaging);
        result.Value.AlreadyTriaging.Should().BeFalse();
        item.Status.Should().Be(RequestStatus.Processing);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldReturnForbidden_WhenAlreadyLinkedBoardIsReadOnly()
    {
        // #1794: the read-only injection vector is reachable without a triage body — capture WITH a
        // board (read-gated at create), then accept. The gate has to sit on the effective board.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload", boardId);

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await _service.EnqueueTriageAsync(userId, item.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("write access");
        item.Status.Should().Be(RequestStatus.Pending);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldPropagateBoardLookupFailure_WhenTargetBoardDoesNotExist()
    {
        // A missing board must surface as the authorization service's own 404, never be swallowed
        // into a generic forbidden.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.NotFound, $"Board with ID {boardId} not found"));

        var result = await _service.EnqueueTriageAsync(userId, item.Id, boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        item.BoardId.Should().BeNull();
        item.Status.Should().Be(RequestStatus.Pending);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldReturnValidationError_WhenBoardlessAndNoTargetBoard()
    {
        // Home quick-capture lands board-less. Accepting it must be rejected synchronously (400),
        // not queued into a doomed async job that fails permanently with a bare FAILED badge (#1764).
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.EnqueueTriageAsync(userId, item.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("board");
        item.Status.Should().Be(RequestStatus.Pending);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldLinkTargetBoardAndTriage_WhenBoardlessCaptureSuppliesBoard()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));

        var result = await _service.EnqueueTriageAsync(userId, item.Id, boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CaptureStatus.Triaging);
        result.Value.AlreadyTriaging.Should().BeFalse();
        item.BoardId.Should().Be(boardId);
        item.Status.Should().Be(RequestStatus.Processing);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldReturnForbidden_WhenTargetBoardNotAccessible()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await _service.EnqueueTriageAsync(userId, item.Id, boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        item.BoardId.Should().BeNull();
        item.Status.Should().Be(RequestStatus.Pending);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldReturnForbidden_WhenTargetBoardIsReadOnlyForCaller()
    {
        // Read-only (Viewer) membership must not be able to queue a proposal into a shared board's
        // review queue — only owners/approvers can clear it (#1794). No board link is written.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture payload");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await _service.EnqueueTriageAsync(userId, item.Id, boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("write access");
        item.BoardId.Should().BeNull();
        item.Status.Should().Be(RequestStatus.Pending);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
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

    // ── BatchTriageAsync ──

    [Fact]
    public async Task BatchTriageAsync_ShouldReturnValidationError_WhenEmptyList()
    {
        var userId = Guid.NewGuid();
        var request = new BatchTriageRequestDto(new List<BatchTriageItemActionDto>());

        var result = await _service.BatchTriageAsync(userId, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task BatchTriageAsync_ShouldReturnValidationError_WhenInvalidAction()
    {
        var userId = Guid.NewGuid();
        var request = new BatchTriageRequestDto(new List<BatchTriageItemActionDto>
        {
            new(Guid.NewGuid(), "invalid_action")
        });

        var result = await _service.BatchTriageAsync(userId, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("invalid_action");
    }

    [Fact]
    public async Task BatchTriageAsync_ShouldReturnValidationError_WhenDuplicateIds()
    {
        var userId = Guid.NewGuid();
        var duplicateId = Guid.NewGuid();
        var request = new BatchTriageRequestDto(new List<BatchTriageItemActionDto>
        {
            new(duplicateId, "triage"),
            new(duplicateId, "ignore")
        });

        var result = await _service.BatchTriageAsync(userId, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Duplicate");
    }

    [Fact]
    public async Task BatchTriageAsync_ShouldProcessMultipleItems_WithPartialFailure()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item1 = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture 1", boardId);
        var item2Id = Guid.NewGuid(); // Non-existent item

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item1.Id, default))
            .ReturnsAsync(item1);
        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item2Id, default))
            .ReturnsAsync((LlmRequest?)null);

        var request = new BatchTriageRequestDto(new List<BatchTriageItemActionDto>
        {
            new(item1.Id, "triage"),
            new(item2Id, "triage")
        });

        var result = await _service.BatchTriageAsync(userId, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(2);
        result.Value.Succeeded.Should().Be(1);
        result.Value.Failed.Should().Be(1);
        result.Value.Results.Should().HaveCount(2);

        result.Value.Results[0].ItemId.Should().Be(item1.Id);
        result.Value.Results[0].Success.Should().BeTrue();

        result.Value.Results[1].ItemId.Should().Be(item2Id);
        result.Value.Results[1].Success.Should().BeFalse();
        result.Value.Results[1].ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task BatchTriageAsync_ShouldSupportIgnoreAction()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "capture to ignore");

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var request = new BatchTriageRequestDto(new List<BatchTriageItemActionDto>
        {
            new(item.Id, "ignore")
        });

        var result = await _service.BatchTriageAsync(userId, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(1);
        result.Value.Failed.Should().Be(0);
    }

    [Fact]
    public async Task BatchTriageAsync_ShouldReturnValidationError_WhenBatchTooLarge()
    {
        var userId = Guid.NewGuid();
        var items = Enumerable.Range(0, 51)
            .Select(i => new BatchTriageItemActionDto(Guid.NewGuid(), "triage"))
            .ToList();
        var request = new BatchTriageRequestDto(items);

        var result = await _service.BatchTriageAsync(userId, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("50");
    }

    [Fact]
    public async Task BatchTriageAsync_ShouldAuthorizeOncePerDistinctBoard_NotOncePerItem()
    {
        // #1836: every board-linked item used to run its own CanWriteBoardAsync (a board fetch plus
        // a membership read). The batch now spends one lookup per DISTINCT board, so five items
        // across two boards cost two lookups, not five.
        var userId = Guid.NewGuid();
        var boardA = Guid.NewGuid();
        var boardB = Guid.NewGuid();

        var items = new[]
        {
            new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "a1", boardA),
            new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "a2", boardA),
            new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "a3", boardA),
            new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "b1", boardB),
            new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "b2", boardB),
        };

        foreach (var item in items)
        {
            _llmQueueRepositoryMock
                .Setup(r => r.GetByIdAsync(item.Id, default))
                .ReturnsAsync(item);
        }

        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success(true));

        var request = new BatchTriageRequestDto(
            items.Select(i => new BatchTriageItemActionDto(i.Id, "triage")).ToList());

        var result = await _service.BatchTriageAsync(userId, request);

        // Behaviour is unchanged: every item still triages.
        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(5);
        result.Value.Failed.Should().Be(0);
        items.Should().OnlyContain(i => i.Status == RequestStatus.Processing);

        _authorizationServiceMock.Verify(s => s.CanWriteBoardAsync(userId, boardA), Times.Once);
        _authorizationServiceMock.Verify(s => s.CanWriteBoardAsync(userId, boardB), Times.Once);
        _authorizationServiceMock.Verify(
            s => s.CanWriteBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task BatchTriageAsync_ShouldDenyEveryItemOnAReadOnlyBoard_WithOneLookup()
    {
        // The memoized outcome must be the FAILURE too, applied uniformly: a Viewer batching three
        // captures on one read-only board gets three identical 403s off a single lookup (#1836).
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        var items = new[]
        {
            new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "one", boardId),
            new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "two", boardId),
            new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "three", boardId),
        };

        foreach (var item in items)
        {
            _llmQueueRepositoryMock
                .Setup(r => r.GetByIdAsync(item.Id, default))
                .ReturnsAsync(item);
        }

        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var request = new BatchTriageRequestDto(
            items.Select(i => new BatchTriageItemActionDto(i.Id, "triage")).ToList());

        var result = await _service.BatchTriageAsync(userId, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(0);
        result.Value.Failed.Should().Be(3);
        result.Value.Results.Should().OnlyContain(r => r.ErrorCode == ErrorCodes.Forbidden);
        result.Value.Results.Should().OnlyContain(r => r.ErrorMessage!.Contains("write access"));
        items.Should().OnlyContain(i => i.Status == RequestStatus.Pending);

        _authorizationServiceMock.Verify(s => s.CanWriteBoardAsync(userId, boardId), Times.Once);
    }

    [Fact]
    public async Task EnqueueTriageAsync_ShouldNotShareAuthorizationAcrossSingleItemCalls()
    {
        // The memo is scoped to one batch call; the single-item path keeps its original
        // one-lookup-per-call shape so a membership change between two accepts is still seen.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var first = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "first", boardId);
        var second = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, "second", boardId);

        _llmQueueRepositoryMock.Setup(r => r.GetByIdAsync(first.Id, default)).ReturnsAsync(first);
        _llmQueueRepositoryMock.Setup(r => r.GetByIdAsync(second.Id, default)).ReturnsAsync(second);
        _authorizationServiceMock
            .Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));

        (await _service.EnqueueTriageAsync(userId, first.Id)).IsSuccess.Should().BeTrue();
        (await _service.EnqueueTriageAsync(userId, second.Id)).IsSuccess.Should().BeTrue();

        _authorizationServiceMock.Verify(s => s.CanWriteBoardAsync(userId, boardId), Times.Exactly(2));
    }

    // ── UpdateSuggestionAsync ──

    [Fact]
    public async Task UpdateSuggestionAsync_ShouldUpdateTextForNewItem()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.Typed, "original text")));

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var dto = new UpdateCaptureSuggestionDto("edited text", "New Title");
        var result = await _service.UpdateSuggestionAsync(userId, item.Id, dto);

        result.IsSuccess.Should().BeTrue();
        result.Value.RawText.Should().Be("edited text");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateSuggestionAsync_ShouldRejectEditAfterTranscriptIsLinked()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeTranscriptV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.TranscriptPaste, "canonical transcript")));
        item.AttachTranscript(Guid.NewGuid());

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.UpdateSuggestionAsync(
            userId,
            item.Id,
            new UpdateCaptureSuggestionDto("attempted edit"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("cannot be edited");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Theory]
    [InlineData(CaptureSource.TranscriptPaste)]
    [InlineData(CaptureSource.TranscriptFile)]
    public async Task UpdateSuggestionAsync_ShouldAllowTranscriptTextAtSourceSpecificLimit(CaptureSource source)
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeTranscriptV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, source, "original transcript")));

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var editedText = new string('t', CaptureRequestContract.MaxTranscriptTextLength);
        var result = await _service.UpdateSuggestionAsync(
            userId,
            item.Id,
            new UpdateCaptureSuggestionDto(editedText));

        result.IsSuccess.Should().BeTrue();
        result.Value.RawText.Should().HaveLength(CaptureRequestContract.MaxTranscriptTextLength);
    }

    [Fact]
    public async Task UpdateSuggestionAsync_ShouldRetainRawTextLimitForNonTranscriptSource()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.Typed, "original text")));

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.UpdateSuggestionAsync(
            userId,
            item.Id,
            new UpdateCaptureSuggestionDto(new string('x', CaptureRequestContract.MaxRawTextLength + 1)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain(CaptureRequestContract.MaxRawTextLength.ToString());
    }

    [Fact]
    public async Task UpdateSuggestionAsync_ShouldRejectEmptyText()
    {
        var userId = Guid.NewGuid();
        var dto = new UpdateCaptureSuggestionDto("   ");

        var result = await _service.UpdateSuggestionAsync(userId, Guid.NewGuid(), dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task UpdateSuggestionAsync_ShouldRejectForbiddenUser()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var item = new LlmRequest(ownerId, CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.Typed, "original")));

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.UpdateSuggestionAsync(callerId, item.Id, new UpdateCaptureSuggestionDto("edited"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task UpdateSuggestionAsync_ShouldRejectTriagingItem()
    {
        var userId = Guid.NewGuid();
        var item = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.Typed, "original")));
        item.MarkAsProcessing();

        _llmQueueRepositoryMock
            .Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        var result = await _service.UpdateSuggestionAsync(userId, item.Id, new UpdateCaptureSuggestionDto("edited"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }
}
