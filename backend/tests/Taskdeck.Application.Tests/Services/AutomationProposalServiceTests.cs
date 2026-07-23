using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AutomationProposalServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IProposalProvenanceRepository> _provenanceRepoMock;
    private readonly Mock<IProposalRevisionRepository> _revisionRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly AutomationProposalService _service;

    public AutomationProposalServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _notificationServiceMock = new Mock<INotificationService>();
        _provenanceRepoMock = new Mock<IProposalProvenanceRepository>();
        _revisionRepoMock = new Mock<IProposalRevisionRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ProposalRevisions).Returns(_revisionRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        // Default: no saved revision, so GetProposalDiffAsync uses the original path.
        // The revision-aware test overrides this per-proposal.
        _revisionRepoMock
            .Setup(r => r.GetLatestByProposalIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((ProposalRevision?)null);
        // The rejected-proposal freeze path (#1439) loads all revisions and filters in memory;
        // default to an empty list so tests that don't seed revisions don't NRE on the repository
        // contract's non-null return.
        _revisionRepoMock
            .Setup(r => r.GetByProposalIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(Array.Empty<ProposalRevision>());
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));
        // Diff now runs the same read-safe permission gates Apply runs
        // (AutomationPolicyEngine.ValidatePermissionsAsync): requester exists, board exists,
        // requester has board access. Default them to PASS so healthy-path diff tests reach
        // their intended gate; the #1398 parity tests below override these to revoke access
        // or delete the board/requester.
        _userRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User("difftester", "diff@example.com", "hashedPassword"));
        _boardRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestDataBuilder.CreateBoard());
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _service = new AutomationProposalService(
            _unitOfWorkMock.Object,
            _notificationServiceMock.Object,
            _provenanceRepoMock.Object);
    }

    // ExpiresAt is private-set on the AutomationProposal aggregate; force it into the past
    // to simulate a proposal that expired before its diff was requested (mirrors the
    // reflection seam used in AutomationProposalServiceEdgeCaseTests).
    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        typeof(AutomationProposal).GetProperty(
            nameof(AutomationProposal.ExpiresAt),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(proposal, expiresAt);
    }

    // DecidedAt is stamped to UtcNow by Reject/Approve; force it to a fixed value so the rejected
    // freeze tests can place revisions deterministically on either side of the decision cutoff.
    private static void SetDecidedAt(AutomationProposal proposal, DateTime decidedAt)
    {
        typeof(AutomationProposal).GetProperty(
            nameof(AutomationProposal.DecidedAt),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(proposal, decidedAt);
    }

    // RevisedAt is stamped to UtcNow in the ProposalRevision ctor; force it so a revision can be
    // placed before or after a proposal's decision time in the rejected freeze tests.
    private static void SetRevisedAt(ProposalRevision revision, DateTimeOffset revisedAt)
    {
        typeof(ProposalRevision).GetProperty(
            nameof(ProposalRevision.RevisedAt),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(revision, revisedAt);
    }

    #region CreateProposalAsync Tests

    [Fact]
    public async Task CreateProposalAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var dto = new CreateProposalDto(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Create new card",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.AddAsync(It.IsAny<AutomationProposal>(), default))
            .ReturnsAsync((AutomationProposal p, CancellationToken ct) => p);

        // Act
        var result = await _service.CreateProposalAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Summary.Should().Be("Create new card");
        result.Value.Status.Should().Be(ProposalStatus.PendingReview);
        result.Value.RiskLevel.Should().Be(RiskLevel.Low);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateProposalAsync_ShouldPersistBaselineProvenance()
    {
        // Arrange
        var operations = new List<CreateProposalOperationDto>
        {
            new(0, "create", "card", "{\"title\":\"Test\"}", "key1")
        };

        var dto = new CreateProposalDto(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Create captured task",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            Operations: operations,
            ProvenanceModelId: "gpt-4.1-mini",
            ProvenanceTotalTokens: 123);

        _proposalRepoMock.Setup(r => r.AddAsync(It.IsAny<AutomationProposal>(), default))
            .ReturnsAsync((AutomationProposal p, CancellationToken ct) => p);

        ProposalProvenance? capturedProvenance = null;
        _provenanceRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ProposalProvenance>(), default))
            .Callback<ProposalProvenance, CancellationToken>((p, _) => capturedProvenance = p)
            .ReturnsAsync((ProposalProvenance p, CancellationToken _) => p);

        // Act
        var result = await _service.CreateProposalAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedProvenance.Should().NotBeNull();
        capturedProvenance!.ProposalId.Should().Be(result.Value.Id);
        capturedProvenance.CorrelationId.Should().Be(dto.CorrelationId);
        capturedProvenance.ModelId.Should().Be("gpt-4.1-mini");
        capturedProvenance.TotalTokens.Should().Be(123);
        capturedProvenance.Fields.Should().Contain(f =>
            f.FieldName == "Summary" &&
            f.Kind == ProvenanceKind.Inferred);
        capturedProvenance.Fields.Should().Contain(f =>
            f.FieldName == "Operation 1: create card" &&
            f.Kind == ProvenanceKind.Inferred);
    }

    [Fact]
    public async Task CreateProposalAsync_ShouldAddOperations_WhenProvided()
    {
        // Arrange
        var operations = new List<CreateProposalOperationDto>
        {
            new(0, "card.create", "Card", "{\"name\":\"Test\"}", "key1"),
            new(1, "card.move", "Card", "{\"position\":5}", "key2", "card-123")
        };

        var dto = new CreateProposalDto(
            ProposalSourceType.Manual,
            Guid.NewGuid(),
            "Multi-step operation",
            RiskLevel.Medium,
            Guid.NewGuid().ToString(),
            Operations: operations);

        _proposalRepoMock.Setup(r => r.AddAsync(It.IsAny<AutomationProposal>(), default))
            .ReturnsAsync((AutomationProposal p, CancellationToken ct) => p);

        // Act
        var result = await _service.CreateProposalAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().HaveCount(2);
        result.Value.Operations[0].Sequence.Should().Be(0);
        result.Value.Operations[1].Sequence.Should().Be(1);
    }

    [Fact]
    public async Task CreateProposalAsync_ShouldReturnValidationError_WhenSummaryIsEmpty()
    {
        // Arrange
        var dto = new CreateProposalDto(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        // Act
        var result = await _service.CreateProposalAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region GetProposalByIdAsync Tests

    [Fact]
    public async Task GetProposalByIdAsync_ShouldReturnProposal_WhenExists()
    {
        // Arrange
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        var proposalId = proposal.Id;

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalByIdAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(proposal.Id);
        result.Value.Summary.Should().Be("Test proposal");
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldBuildReadablePresentation_WhenOperationsExist()
    {
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Create the onboarding follow-up",
            RiskLevel.High,
            Guid.NewGuid().ToString(),
            boardId,
            sourceReferenceId: Guid.NewGuid().ToString());
        var proposalId = proposal.Id;

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "card.create",
            "Card",
            "{\"title\":\"Draft follow-up\"}",
            Guid.NewGuid().ToString()));
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            1,
            "board.rename",
            "Board",
            "{\"name\":\"Support follow-up\"}",
            Guid.NewGuid().ToString(),
            boardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var result = await _service.GetProposalByIdAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Presentation.PlainSummary.Should().Contain("apply 2 planned changes");
        result.Value.Presentation.SourceCue.Should().Be("Created from Inbox capture triage.");
        result.Value.Presentation.RiskCue.Should().Contain("High risk");
        result.Value.Presentation.OperationHeadlines.Should().ContainInOrder(
            "Create card \"Draft follow-up\".",
            $"Rename board \"Support follow-up\".");
        result.Value.Presentation.AffectedEntities.Should().Contain(entity =>
            entity.EntityType == "Board" &&
            entity.EntityId == boardId.ToString() &&
            entity.Label == "Board \"Support follow-up\"" &&
            entity.ChangeCount == 1);
        result.Value.Presentation.AffectedEntities.Should().Contain(entity =>
            entity.EntityType == "Card" &&
            entity.Label == "Card \"Draft follow-up\"" &&
            entity.ChangeCount == 1);
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldFallBackToEntityId_WhenParametersLackName()
    {
        var targetId = Guid.NewGuid().ToString();
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Update the card",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "card.update",
            "Card",
            "{}",
            Guid.NewGuid().ToString(),
            targetId));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _service.GetProposalByIdAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Presentation.AffectedEntities.Should().ContainSingle(entity =>
            entity.EntityType == "Card" &&
            entity.Label == $"Card {targetId}");
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldPreserveNamedTargetCasing_InSingleOperationSummary()
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Create the follow-up card",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        var proposalId = proposal.Id;

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "card.create",
            "Card",
            "{\"title\":\"Draft Follow-Up\"}",
            Guid.NewGuid().ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var result = await _service.GetProposalByIdAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Presentation.PlainSummary.Should().Be(
            "Create the follow-up card This would create card \"Draft Follow-Up\".");
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldRenderCaptureTriageTaskBatch_InBusinessLanguage()
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Capture triage (2 tasks): Captured note for client onboarding.",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        var proposalId = proposal.Id;

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "create",
            "card",
            "{\"title\":\"Request director ID documents\"}",
            "card-0"));
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            1,
            "create",
            "card",
            "{\"title\":\"Send engagement letter\"}",
            "card-1"));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var result = await _service.GetProposalByIdAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Presentation.PlainSummary.Should().Be("Create 2 task cards from the captured note.");
        result.Value.Presentation.ImpactSummary.Should().Be("2 task card changes ready for approval.");
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync((AutomationProposal?)null);

        // Act
        var result = await _service.GetProposalByIdAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region ApproveProposalAsync Tests

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnSuccess_WhenPending()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        // A fully valid proposal (#1416): approve now runs Apply's structure AND
        // permission/contract gates, so the happy path must carry an operation that clears both
        // (an in-scope board update), with the fixture's requester/board/access defaults passing.
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "board",
            System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Renamed board" }),
            Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, deciderId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Approved);
        result.Value.DecidedByUserId.Should().Be(deciderId);
        result.Value.DecidedAt.Should().NotBeNull();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
        _notificationServiceMock.Verify(
            s => s.PublishAsync(
                It.Is<CreateNotificationRequestDto>(n =>
                    n.UserId == proposal.RequestedByUserId &&
                    n.Type == NotificationType.ProposalOutcome),
                default),
            Times.Once);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldRejectZeroOperationProposal_MatchingApplyStructureGate()
    {
        // #1416 approve == apply: a zero-operation PendingReview proposal previously approved
        // cleanly (status → Approved) and only failed later at Apply with 400 "Proposal must
        // contain at least one operation". Approve now runs the SAME structure gate Apply runs via
        // AutomationPolicyEngine.ValidateOperationStructure (and that GetProposalDiffAsync mirrors),
        // rejecting it with the identical ValidationError before the transition commits.
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Zero-op proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, Guid.NewGuid());

        // Assert: same failure Apply's structure validation produces, and the transition is refused.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Proposal must contain at least one operation");
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
        _notificationServiceMock.Verify(
            s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldRejectExpiredProposal_WithConflict()
    {
        // #1416 expiry contract: an expired PendingReview proposal must not be approvable. Approve
        // enforces this through the domain transition itself (AutomationProposal.Approve throws
        // InvalidOperation → 409), the established approve-time expiry semantics this slice
        // preserves — deliberately NOT the diff/preview path's 400 "Proposal has expired" read
        // shape, because approving is a state transition and 409 conflict is the correct code for
        // refusing to advance an expired proposal. The proposal carries an operation so it clears
        // the structure gate and the expiry guard is what rejects it.
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Expiring proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", "{\"title\":\"Test\"}", Guid.NewGuid().ToString()));
        // Force the proposal past its expiry without changing status (mirrors a proposal that
        // expired while pending). ExpiresAt is private-set on the entity.
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, Guid.NewGuid());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("expired");
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldValidateEffectiveRevision_NotOriginalOperations()
    {
        // #1416 approve == apply, revision-aware: Apply executes the latest saved revision
        // (AutomationExecutorService.MaterializeEffectiveProposalAsync), so approve's structure
        // gate must validate that SAME effective set. A proposal whose ORIGINAL operations are
        // empty but whose latest revision is valid is approvable — matching Apply — rather than
        // being falsely rejected by the zero-op check on the stale original operations.
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Originally empty, revised valid",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var revisedPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType = "update",
                    targetType = "board",
                    targetId = boardId.ToString(),
                    parameters = System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Revised name" }),
                    idempotencyKey = Guid.NewGuid().ToString()
                }
            }
        });
        var revision = new ProposalRevision(proposal.Id, 1, deciderId, revisedPayload, "Add an operation");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);
        _revisionRepoMock
            .Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default))
            .ReturnsAsync(revision);

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, deciderId);

        // Assert: the valid effective revision clears the structure gate and the proposal approves.
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Approved);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldPinLatestRevision_AndEchoEffectiveOperations()
    {
        // #1428 + #1424: approve stamps ApprovedRevisionId with the latest revision read at approve
        // time (so Apply materializes exactly that one), and the approve response echoes the
        // effective (revised) operations rather than the stale original ones.
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Revised then approved",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "board",
            System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Original name" }),
            Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));

        var revisedPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType = "update",
                    targetType = "board",
                    targetId = boardId.ToString(),
                    parameters = System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Revised name" }),
                    idempotencyKey = Guid.NewGuid().ToString()
                }
            }
        });
        var revision = new ProposalRevision(proposal.Id, 1, deciderId, revisedPayload, "Rename differently");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _revisionRepoMock.Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default)).ReturnsAsync(revision);
        _revisionRepoMock.Setup(r => r.GetByIdAsync(revision.Id, default)).ReturnsAsync(revision);

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, deciderId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Approved);
        result.Value.ApprovedRevisionId.Should().Be(revision.Id);
        proposal.ApprovedRevisionId.Should().Be(revision.Id);
        result.Value.Operations.Should().ContainSingle();
        result.Value.Operations[0].Parameters.Should().Contain("Revised name");
        result.Value.Operations[0].Parameters.Should().NotContain("Original name");
        // Split-brain guard: the presentation block derives from the same effective set as
        // Operations, so it must describe the revised content, not the stale original.
        result.Value.Presentation.OperationHeadlines.Should().ContainSingle(h => h.Contains("Revised name"));
        result.Value.Presentation.OperationHeadlines.Should().NotContain(h => h.Contains("Original name"));
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldLeaveApprovedRevisionIdNull_WhenNoRevisionExists()
    {
        // #1428: approving a proposal that has no saved revision pins nothing (null), so Apply
        // materializes the original operations and later revisions cannot change what it runs.
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Approved from original",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "board",
            System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Original name" }),
            Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        // Default revision mock: GetLatestByProposalIdAsync returns null (no revision).

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, deciderId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ApprovedRevisionId.Should().BeNull();
        proposal.ApprovedRevisionId.Should().BeNull();
        result.Value.Operations.Should().ContainSingle();
        result.Value.Operations[0].Parameters.Should().Contain("Original name");
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldRejectRevokedBoardAccess_MatchingApplyPermissionGate()
    {
        // #1416 trust-class completion: Apply runs ValidatePermissionsAsync after the policy gate,
        // so a proposal whose requester lost board access mid-review is rejected 403 at Apply (and,
        // since #1413, at diff). Approve previously ran no permission gate, so the reviewer's
        // approval succeeded 200 and only Apply failed 403. Approve now runs the same gate:
        // approve == apply (403, same message).
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        // Requester exists and the board exists (constructor defaults), but access is revoked.
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act (approve)
        var approveResult = await _service.ApproveProposalAsync(proposalId, Guid.NewGuid());

        // Act (apply-side permission gate) on the equivalent operation DTOs.
        var applyResult = await new AutomationPolicyEngine(_unitOfWorkMock.Object).ValidatePermissionsAsync(
            requesterId, boardId, BuildPermissionGateApplyOperations(proposalId, boardId));

        // Assert: approve rejects, and rejects identically to Apply (403, same message).
        applyResult.IsSuccess.Should().BeFalse();
        applyResult.ErrorCode.Should().Be(ErrorCodes.Forbidden);

        approveResult.IsSuccess.Should().BeFalse();
        approveResult.ErrorCode.Should().Be(applyResult.ErrorCode);
        approveResult.ErrorMessage.Should().Be(applyResult.ErrorMessage);
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldRejectDeletedBoard_MatchingApplyPermissionGate()
    {
        // #1416: a proposal whose board was deleted mid-review is rejected 404 at Apply
        // (ValidatePermissionsAsync board-existence gate) and at diff (#1413). Approve now runs
        // the same gate instead of approving 200 and failing 404 at Apply.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        // Board no longer exists (overrides the constructor default for this board id).
        _boardRepoMock
            .Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board?)null);

        var approveResult = await _service.ApproveProposalAsync(proposalId, Guid.NewGuid());
        var applyResult = await new AutomationPolicyEngine(_unitOfWorkMock.Object).ValidatePermissionsAsync(
            requesterId, boardId, BuildPermissionGateApplyOperations(proposalId, boardId));

        applyResult.IsSuccess.Should().BeFalse();
        applyResult.ErrorCode.Should().Be(ErrorCodes.NotFound);

        approveResult.IsSuccess.Should().BeFalse();
        approveResult.ErrorCode.Should().Be(applyResult.ErrorCode);
        approveResult.ErrorMessage.Should().Be(applyResult.ErrorMessage);
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldRejectContractViolation_OnRevisedOperations()
    {
        // #1416: ValidatePermissionsAsync ends with ProposalOperationContractValidator, so a
        // saved revision whose operations violate the operation contract (here: a board update
        // with no updatable fields) is rejected 400 at Apply. Approve validates the same
        // effective revised set through the same engine call, so the reviewer cannot approve it.
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Valid original, contract-violating revision",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "board",
            System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Valid original" }),
            Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));

        var revisedPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType = "update",
                    targetType = "board",
                    targetId = boardId.ToString(),
                    // No 'name', 'description', or 'isArchived': fails the operation contract.
                    parameters = System.Text.Json.JsonSerializer.Serialize(new { boardId }),
                    idempotencyKey = Guid.NewGuid().ToString()
                }
            }
        });
        var revision = new ProposalRevision(proposal.Id, 1, deciderId, revisedPayload, "Strip fields");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _revisionRepoMock
            .Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default))
            .ReturnsAsync(revision);

        var result = await _service.ApproveProposalAsync(proposalId, deciderId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be(
            "Update board operation requires at least one of 'name', 'description', or 'isArchived'");
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldReportExpiry_NotForbidden_WhenExpiredAndAccessRevoked()
    {
        // Gate-ordering pin (mirrors the #1413 LOW-4 pin on the diff path, adapted to approve's
        // 409 expiry semantics): a proposal that is BOTH expired AND has revoked requester access
        // must fail with the expiry 409 InvalidOperation — never Forbidden — because approve runs
        // structure → expiry → permissions in the same order as diff/apply. If someone reorders
        // the permission gate ahead of the expiry short-circuit, this test fails on Forbidden.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.ApproveProposalAsync(proposalId, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorCode.Should().NotBe(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("expired");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnValidationError_WhenSavedRevisionPayloadIsInvalid()
    {
        // #1416 defensive branch pin: a saved revision is validated at save time, so a malformed
        // RevisedPayload should be unreachable — but if the effective payload cannot be
        // materialized, Apply fails 400 (MaterializeEffectiveProposalAsync), so approve must
        // surface the identical ValidationError instead of approving a proposal Apply will refuse.
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Valid original, corrupt revision",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "board",
            System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Valid original" }),
            Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));

        var revision = new ProposalRevision(proposal.Id, 1, deciderId, "{not valid json", "Corrupt");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _revisionRepoMock
            .Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default))
            .ReturnsAsync(revision);

        var result = await _service.ApproveProposalAsync(proposalId, deciderId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("RevisedPayload must be valid JSON");
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldRejectProposalRevisedToEmpty_MatchingApplyGate()
    {
        // #1416 inverse revision-aware direction: an originally-VALID proposal whose latest
        // revision materializes to zero operations must be rejected at approve, exactly as Apply
        // rejects it when materializing the effective revision — validating only the original
        // operations would approve a proposal the executor refuses. Locks both directions of the
        // revision-aware gate together with ShouldValidateEffectiveRevision_NotOriginalOperations.
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Valid original, revised to empty",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "board",
            System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Valid original" }),
            Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));

        var revision = new ProposalRevision(
            proposal.Id, 1, deciderId, "{\"operations\":[]}", "Strip all operations");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _revisionRepoMock
            .Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default))
            .ReturnsAsync(revision);

        var result = await _service.ApproveProposalAsync(proposalId, deciderId);

        // Same failure Apply produces when the effective revision has no operations.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("RevisedPayload operations must contain at least one operation");
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnInvalidOperation_WhenAlreadyApproved()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.Approve(deciderId);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.ApproveProposalAsync(proposalId, Guid.NewGuid());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region RejectProposalAsync Tests

    [Fact]
    public async Task RejectProposalAsync_ShouldReturnSuccess_WhenPending()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.RejectProposalAsync(
            proposalId,
            deciderId,
            new UpdateProposalStatusDto("Not needed"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Rejected);
        result.Value.DecidedByUserId.Should().Be(deciderId);
        result.Value.FailureReason.Should().Be("Not needed");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RejectProposalAsync_ShouldRequireReason_ForHighRisk()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.High,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.RejectProposalAsync(
            proposalId,
            deciderId,
            new UpdateProposalStatusDto());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region DeferProposalAsync Tests

    [Fact]
    public async Task DeferProposalAsync_ShouldReturnSuccess_AndSetDeferredUntil_KeepingPendingReview()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.DeferProposalAsync(proposalId, TimeSpan.FromMinutes(60));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.PendingReview);
        result.Value.DeferredUntil.Should().NotBeNull();
        result.Value.DecidedByUserId.Should().BeNull();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
        // Defer is not a decision: no notification and no outcome are written.
        _notificationServiceMock.Verify(
            s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeferProposalAsync_ShouldReturnNotFound_WhenProposalMissing()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync((AutomationProposal?)null);

        // Act
        var result = await _service.DeferProposalAsync(proposalId, TimeSpan.FromMinutes(60));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task DeferProposalAsync_ShouldReturnInvalidOperation_WhenNotPendingReview()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        proposal.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.DeferProposalAsync(proposalId, TimeSpan.FromMinutes(60));

        // Assert (InvalidOperation -> 409)
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task DeferProposalAsync_ShouldReturnValidationError_WhenDurationOutOfRange()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act (zero duration -> domain ValidationError -> 400)
        var result = await _service.DeferProposalAsync(proposalId, TimeSpan.Zero);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task DeferProposalAsync_ShouldMapConcurrencyConflictTo409_NotUnhandled()
    {
        // Arrange — a concurrent decide+defer/double-submit collides on the UpdatedAt
        // concurrency token. UnitOfWork.SaveChangesAsync converts the underlying
        // DbUpdateConcurrencyException into DomainException(Conflict); the service's
        // DomainException catch then returns a 409-class failure rather than a 500.
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default))
            .ThrowsAsync(new DomainException(ErrorCodes.Conflict, "Record was updated by another session. Refresh and retry your action."));

        // Act
        var result = await _service.DeferProposalAsync(proposalId, TimeSpan.FromMinutes(60));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    #endregion

    #region MarkAsAppliedAsync Tests

    [Fact]
    public async Task MarkAsAppliedAsync_ShouldReturnSuccess_WhenApproved()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.MarkAsAppliedAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Applied);
        result.Value.AppliedAt.Should().NotBeNull();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task MarkAsAppliedAsync_ShouldReturnInvalidOperation_WhenNotApproved()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.MarkAsAppliedAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }

    #endregion

    #region MarkAsFailedAsync Tests

    [Fact]
    public async Task MarkAsFailedAsync_ShouldReturnSuccess_WhenApproved()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.MarkAsFailedAsync(proposalId, "Database error");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProposalStatus.Failed);
        result.Value.FailureReason.Should().Be("Database error");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    #endregion

    #region ExpireProposalsAsync Tests

    [Fact]
    public async Task ExpireProposalsAsync_ShouldExpireAllStaleProposals()
    {
        // Arrange
        var proposal1 = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test 1",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1);

        var proposal2 = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test 2",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1);

        // Simulate that these are expired (repository would return expired ones)
        _proposalRepoMock.Setup(r => r.GetExpiredAsync(default))
            .ReturnsAsync(new[] { proposal1, proposal2 });

        // Act
        var result = await _service.ExpireProposalsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task ExpireProposalsAsync_ShouldReturnZero_WhenNoExpiredProposals()
    {
        // Arrange
        _proposalRepoMock.Setup(r => r.GetExpiredAsync(default))
            .ReturnsAsync(Array.Empty<AutomationProposal>());

        // Act
        var result = await _service.ExpireProposalsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region IsExpired DTO Tests

    [Fact]
    public async Task GetProposalByIdAsync_ShouldSetIsExpiredTrue_WhenProposalHasPassedExpiresAt()
    {
        // Arrange
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Expired proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1);
        var proposalId = proposal.Id;

        // Force the ExpiresAt into the past
        var expiresAtProperty = typeof(AutomationProposal).GetProperty("ExpiresAt");
        expiresAtProperty!.SetValue(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalByIdAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldSetIsExpiredFalse_WhenProposalHasNotExpired()
    {
        // Arrange
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Fresh proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1440);
        var proposalId = proposal.Id;

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalByIdAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsExpired.Should().BeFalse();
    }

    #endregion

    #region DismissProposalsAsync Tests

    [Fact]
    public async Task DismissProposalsAsync_ShouldDismissExpiredApprovedProposal()
    {
        // Arrange
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Approved but expired",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1);

        proposal.Approve(Guid.NewGuid());

        // Force the ExpiresAt into the past
        var expiresAtProperty = typeof(AutomationProposal).GetProperty("ExpiresAt");
        expiresAtProperty!.SetValue(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), default))
            .ReturnsAsync(new[] { proposal });

        // Act
        var result = await _service.DismissProposalsAsync(new[] { proposal.Id }, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        proposal.Status.Should().Be(ProposalStatus.Dismissed);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DismissProposalsAsync_ShouldSkipNonExpiredApprovedProposal()
    {
        // Arrange
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Approved and still valid",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1440);

        proposal.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), default))
            .ReturnsAsync(new[] { proposal });

        // Act
        var result = await _service.DismissProposalsAsync(new[] { proposal.Id }, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        proposal.Status.Should().Be(ProposalStatus.Approved);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task DismissProposalsAsync_ShouldDismissTerminalProposals()
    {
        // Arrange
        var expired = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Expired one",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: 1);
        expired.Expire();

        var applied = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Applied one",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        applied.Approve(Guid.NewGuid());
        applied.MarkAsApplied();

        _proposalRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), default))
            .ReturnsAsync(new[] { expired, applied });

        // Act
        var result = await _service.DismissProposalsAsync(new[] { expired.Id, applied.Id }, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        expired.Status.Should().Be(ProposalStatus.Dismissed);
        applied.Status.Should().Be(ProposalStatus.Dismissed);
    }

    #endregion

    #region GetProposalDiffAsync Tests

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReturnStoredPreview_ForWellFormedNonEmptyProposal()
    {
        // Back-compat regression (#1376): a non-expired, well-formed proposal with a
        // stored DiffPreview still returns that preview byte-for-byte through the cached
        // fast path — the new expiry/structure gates run ahead of it but pass cleanly, so
        // behavior is unchanged for the healthy case.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { name = "Renamed board", boardId });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "board", parameters, Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));
        proposal.SetDiffPreview("+ New card created\n- Old card removed");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: the stored preview is returned unchanged.
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be("+ New card created\n- Old card removed");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectZeroOperationProposal_WhenNoStoredPreview()
    {
        // #1376 asymmetry (2): a zero-operation proposal previously returned 404
        // "Diff preview not available for this proposal" here, while Apply's structure
        // gate rejects it with 400 "Proposal must contain at least one operation". Preview
        // now runs the same structure gate and returns the identical ValidationError.
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: same failure Apply's structure validation produces.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Proposal must contain at least one operation");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectZeroOperationProposal_EvenWithStoredPreview()
    {
        // #1376 asymmetry (2), cached-preview fast path: a zero-operation proposal that
        // carries a stored DiffPreview previously previewed 200 with that stale preview,
        // yet Apply always rejects it. The structure gate now rejects it BEFORE the cached
        // preview is consulted, so preview == apply (400 "must contain at least one
        // operation") whether or not a DiffPreview is stored.
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        proposal.SetDiffPreview("0. Create card \"Stale preview\"");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Proposal must contain at least one operation");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectExpiredProposal_MatchingApplyPolicyGate()
    {
        // #1376 asymmetry (1): GetProposalDiffAsync never checked ExpiresAt, so an expired
        // proposal previewed a clean diff and then failed Apply after approval. Preview now
        // runs the same expiry gate the executor runs via AutomationPolicyEngine.ValidatePolicy.
        // True parity contract: the SAME expired proposal is fed to both trust boundaries and
        // the ErrorCode + ErrorMessage must be identical — no drift allowed.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { name = "Renamed board", boardId });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "board", parameters, Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));
        // Force the proposal past its expiry without changing status (mirrors a proposal
        // that expired between preview requests). ExpiresAt is private-set on the entity.
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act (preview)
        var previewResult = await _service.GetProposalDiffAsync(proposalId);

        // Act (apply-side policy gate) on an equivalent DTO — the expiry semantics live here.
        var applyOperations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), proposal.Id, 0, "update", "board", boardId.ToString(), parameters, Guid.NewGuid().ToString(), null)
        };
        var applyProposal = new ProposalDto(
            proposal.Id,
            ProposalSourceType.Chat,
            null,
            boardId,
            Guid.NewGuid(),
            ProposalStatus.Approved,
            RiskLevel.Low,
            "Test proposal",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            proposal.ExpiresAt,
            null,
            null,
            null,
            null,
            Guid.NewGuid().ToString(),
            applyOperations);
        var applyResult = new AutomationPolicyEngine(_unitOfWorkMock.Object).ValidatePolicy(applyProposal);

        // Assert: preview rejects, and rejects identically to Apply.
        applyResult.IsSuccess.Should().BeFalse();
        applyResult.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        applyResult.ErrorMessage.Should().Be("Proposal has expired");

        previewResult.IsSuccess.Should().BeFalse();
        previewResult.ErrorCode.Should().Be(applyResult.ErrorCode);
        previewResult.ErrorMessage.Should().Be(applyResult.ErrorMessage);
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectExpiredProposal_EvenWithStoredPreview()
    {
        // #1376 asymmetry (1), cached-preview fast path: an expired proposal with a stored
        // DiffPreview previously previewed 200 with that preview and then failed Apply. The
        // expiry gate now runs ahead of the cached-preview return, so preview == apply.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { name = "Renamed board", boardId });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "board", parameters, Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));
        proposal.SetDiffPreview("0. Update board \"Renamed board\"");
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: the cached preview is NOT returned; the expiry rejection is.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Proposal has expired");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public async Task GetProposalDiffAsync_ShouldReturnValidationError_ForNonObjectParameters(string parameters)
    {
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Invalid parameters",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "create",
            "card",
            parameters,
            Guid.NewGuid().ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);

        var result = await _service.GetProposalDiffAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("JSON object");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReturnReadableDescriptions_ForCreateCardOperations()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "To Do", 0);
        var columnId = column.Id;
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Create task card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = "Fix login bug",
            description = "Users cannot log in",
            columnId,
            boardId
        });

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", parameters, Guid.NewGuid().ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(columnId, default))
            .ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new[] { column });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(Array.Empty<Card>());
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Create");
        result.Value.Should().Contain("Fix login bug");
        result.Value.Should().Contain("To Do");
        result.Value.Should().NotContain(columnId.ToString());
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReturnReadableDescriptions_ForMoveCardOperations()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "In Progress", 1);
        var columnId = column.Id;
        var cardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Move card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new
        {
            cardId,
            columnId
        });

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "move", "card", parameters, Guid.NewGuid().ToString(),
            targetId: cardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(columnId, default))
            .ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new[] { column });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        var cardRepoMock = new Mock<ICardRepository>();
        var card = new Card(cardId, boardId, columnId, "Fix login bug");
        cardRepoMock.Setup(r => r.GetByIdAsync(cardId, default))
            .ReturnsAsync(card);
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new[] { card });
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Move");
        result.Value.Should().Contain("Fix login bug");
        result.Value.Should().Contain("In Progress");
        result.Value.Should().NotContain(cardId.ToString());
        result.Value.Should().NotContain(columnId.ToString());
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldSurfaceDestinationPosition_ForColumnReorderOperations()
    {
        // Arrange: a 3-column board with a STRICTLY INTERIOR destination (position 1;
        // the clamp ceiling is 2). Interior beats the ceiling here: a degenerate bug that
        // always rendered the ceiling would still pass at position 2, but fails at 1.
        // (Previously this test used a single-column board with position 2 — an
        // out-of-range target that Apply clamps to the end — and asserted the raw
        // requested value, locking in the preview != apply divergence this issue fixes.
        // See the clamp-specific test below.)
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var todo = new Column(boardId, "To Do", 0);
        var column = new Column(boardId, "In Progress", 1);
        var done = new Column(boardId, "Done", 2);
        var columnId = column.Id;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Reorder column",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { columnId, position = 1 });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "reorder", "column", parameters, Guid.NewGuid().ToString(),
            targetId: columnId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(columnId, default)).ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { todo, column, done });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Card>());
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        var labelRepoMock = new Mock<ILabelRepository>();
        labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());
        _unitOfWorkMock.Setup(u => u.Labels).Returns(labelRepoMock.Object);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: the approval preview names the column and its requested interior
        // destination — not the clamp ceiling (2).
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Contain("Reorder");
        result.Value.Should().Contain("In Progress");
        result.Value.Should().Contain("to position 1");
        result.Value.Should().NotContain("to position 2");
        result.Value.Should().NotContain(columnId.ToString());
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldSurfaceClampedEffectivePosition_WhenColumnReorderOvershoots()
    {
        // Arrange: a 3-column board with a reorder targeting position 99. ColumnService
        // clamps an overshooting target to the end (Math.Min(position, columnCount - 1) = 2),
        // so the preview must show the clamped effective destination — not the raw 99 — to
        // stay equal to what Apply does (#1370 preview == apply).
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var todo = new Column(boardId, "To Do", 0);
        var column = new Column(boardId, "In Progress", 1);
        var done = new Column(boardId, "Done", 2);
        var columnId = column.Id;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Reorder column",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { columnId, position = 99 });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "reorder", "column", parameters, Guid.NewGuid().ToString(),
            targetId: columnId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(columnId, default)).ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { todo, column, done });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Card>());
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        var labelRepoMock = new Mock<ILabelRepository>();
        labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());
        _unitOfWorkMock.Setup(u => u.Labels).Returns(labelRepoMock.Object);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: preview shows the clamped effective destination (2), never the raw 99.
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Contain("In Progress");
        result.Value.Should().Contain("to position 2");
        result.Value.Should().NotContain("position 99");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectOriginalProposalViolatingStructureLimits()
    {
        // Arrange: an original proposal with duplicate operation sequences violates the
        // structure invariants Apply enforces (ValidatePolicy -> ValidateOperationStructure).
        // Preview must fail with the same ValidationError instead of rendering cleanly and
        // failing only at Apply (#1370 preview == apply).
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Malformed structure",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { name = "Renamed", boardId });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "board", parameters, Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "board", parameters, Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: same failure Apply's structure validation produces.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("sequences must be unique");
    }

    [Fact]
    public async Task ProposalReorderPreview_ShouldMatchApplyOutcome_ForOvershootingPosition()
    {
        // True parity contract for #1370: the destination rendered in the preview is
        // parsed back out of the diff text and compared against the position
        // ColumnService actually applies on the same board state. There is no shared
        // hardcoded expected value — if the preview clamp and the apply clamp ever
        // drift apart, this test fails regardless of which side moved.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var todo = new Column(boardId, "To Do", 0);
        var column = new Column(boardId, "In Progress", 1);
        var done = new Column(boardId, "Done", 2);
        var columnId = column.Id;
        const int requestedPosition = 99;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Reorder column",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { columnId, position = requestedPosition });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "reorder", "column", parameters, Guid.NewGuid().ToString(),
            targetId: columnId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(columnId, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { todo, column, done });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Card>());
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        var labelRepoMock = new Mock<ILabelRepository>();
        labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Label>());
        _unitOfWorkMock.Setup(u => u.Labels).Returns(labelRepoMock.Object);

        // Act 1: preview — extract the rendered destination position from the diff text.
        var diffResult = await _service.GetProposalDiffAsync(proposalId);
        diffResult.IsSuccess.Should().BeTrue(diffResult.ErrorMessage);
        var match = System.Text.RegularExpressions.Regex.Match(diffResult.Value, @"to position (\d+)");
        match.Success.Should().BeTrue($"the preview should surface a destination position, but was: {diffResult.Value}");
        var previewedPosition = int.Parse(match.Groups[1].Value);

        // Act 2: apply — execute the same reorder via ColumnService on the same board.
        var columnService = new ColumnService(_unitOfWorkMock.Object);
        var applyResult = await columnService.ReorderColumnAsync(columnId, requestedPosition);

        // Assert: what the reviewer approved is exactly what Apply executed.
        applyResult.IsSuccess.Should().BeTrue(applyResult.ErrorMessage);
        column.Position.Should().Be(previewedPosition,
            "the destination position shown in the approval preview must equal the position Apply lands on");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldPreviewPositionZero_ForReorderOnSingleColumnBoard()
    {
        // Single-column board: the only valid slot is 0, so any requested destination
        // previews as the clamped "to position 0" — exactly where Apply leaves the column.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Only", 0);
        var columnId = column.Id;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Reorder column",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { columnId, position = 5 });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "reorder", "column", parameters, Guid.NewGuid().ToString(),
            targetId: columnId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(columnId, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { column });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Card>());
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        var labelRepoMock = new Mock<ILabelRepository>();
        labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Label>());
        _unitOfWorkMock.Setup(u => u.Labels).Returns(labelRepoMock.Object);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Contain("to position 0");
        result.Value.Should().NotContain("position 5");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldFallBackToRawPosition_WhenBoardColumnLookupFails()
    {
        // Pins the documented degraded path: when the best-effort board-column lookup
        // fails (BuildReadableDiffAsync swallows the exception and renders with empty
        // lookups), the preview cannot compute the clamp and renders the RAW requested
        // position — strictly no worse than the pre-#1370 behavior, which always
        // rendered raw. Contract validation still passes because it resolves the column
        // via GetByIdAsync, which succeeds here; only GetByBoardIdAsync (the diff's
        // name/clamp lookup) fails.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "In Progress", 1);
        var columnId = column.Id;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Reorder column",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { columnId, position = 99 });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "reorder", "column", parameters, Guid.NewGuid().ToString(),
            targetId: columnId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(columnId, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("column lookup unavailable"));
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: degraded preview still renders, with the raw requested position.
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Contain("to position 99");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldPreviewProposalAtMaxOperationCount()
    {
        // Boundary-PASS through the new structure gate: exactly 50 operations (the
        // MaxOperationCount ceiling) must preview cleanly — the gate rejects only >50.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Bulk board renames",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        for (var i = 0; i < 50; i++)
        {
            var parameters = System.Text.Json.JsonSerializer.Serialize(new { boardId, name = $"Rename {i}" });
            proposal.AddOperation(new AutomationProposalOperation(
                proposal.Id, i, "update", "board", parameters, Guid.NewGuid().ToString(),
                targetId: boardId.ToString()));
        }

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Column>());
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Card>());
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        var labelRepoMock = new Mock<ILabelRepository>();
        labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Label>());
        _unitOfWorkMock.Setup(u => u.Labels).Returns(labelRepoMock.Object);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: previews cleanly with one line per operation.
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Split(Environment.NewLine).Should().HaveCount(50);
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldDescribeDueDateExactlyAsApplyNormalizesIt()
    {
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "To Do", 0);
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Create dated card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        var parameters = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = "File return",
            columnId = column.Id,
            boardId,
            dueDate = "2026-07-14T09:30:00+02:00"
        });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", parameters, Guid.NewGuid().ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(column.Id, default)).ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);
        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Card>());
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        var result = await _service.GetProposalDiffAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("set due date to 2026-07-14T07:30:00.0000000+00:00");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldResolveUpdateLabelIdsToNames()
    {
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "To Do", 0);
        var card = new Card(Guid.NewGuid(), boardId, column.Id, "File return");
        var urgent = new Label(boardId, "urgent", "#FF0000");
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Replace card labels",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        var parameters = System.Text.Json.JsonSerializer.Serialize(new
        {
            cardId = card.Id,
            labelIds = new[] { urgent.Id }
        });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "card",
            parameters,
            Guid.NewGuid().ToString(),
            targetId: card.Id.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { column });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);
        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default)).ReturnsAsync(card);
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { card });
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);
        var labelRepoMock = new Mock<ILabelRepository>();
        labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { urgent });
        _unitOfWorkMock.Setup(u => u.Labels).Returns(labelRepoMock.Object);

        var result = await _service.GetProposalDiffAsync(proposalId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Contain("replace labels with [\"urgent\"]");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldDescribeCardLabelOperation()
    {
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var card = new Card(boardId, Guid.NewGuid(), "File return");
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Label card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        var parameters = System.Text.Json.JsonSerializer.Serialize(new
        {
            cardId = card.Id,
            labelName = "urgent"
        });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "add-label",
            "card",
            parameters,
            Guid.NewGuid().ToString(),
            targetId: card.Id.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Column>());
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);
        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default)).ReturnsAsync(card);
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { card });
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);
        var labelRepoMock = new Mock<ILabelRepository>();
        labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new[] { new Label(boardId, "urgent", "#FF0000") });
        _unitOfWorkMock.Setup(u => u.Labels).Returns(labelRepoMock.Object);

        var result = await _service.GetProposalDiffAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Add label \"urgent\" to card \"File return\"");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectCreateCardMissingApplyFields_WhenBoardIdIsNull()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Update something",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId: null);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = "My card title"
        });

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", parameters, Guid.NewGuid().ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act — no column/card repos set up since the executable fields are absent.
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert — preview rejects the same payload Apply cannot execute.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("'columnId'");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReflectSavedRevision_NotOriginalOperationsOrStoredPreview()
    {
        // Arrange: a proposal whose ORIGINAL operation AND stored DiffPreview both
        // describe "Original card", plus a saved revision whose operation describes
        // "Revised card". Apply materializes the latest revision
        // (AutomationExecutorService.MaterializeEffectiveProposalAsync), so the diff
        // preview must describe the REVISED operation — not the original ops and not
        // the stale stored preview (#1235, exit criterion (b): preview == apply).
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "To Do", 0);
        var columnId = column.Id;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Create card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var originalParams = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = "Original card",
            columnId,
            boardId
        });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", originalParams, Guid.NewGuid().ToString()));
        proposal.SetDiffPreview("0. Create card \"Original card\"");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var revisedParams = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = "Revised card",
            columnId,
            boardId
        });
        var revisedPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType = "create",
                    targetType = "card",
                    parameters = revisedParams,
                    idempotencyKey = Guid.NewGuid().ToString()
                }
            }
        });
        var revision = new ProposalRevision(proposalId, 1, Guid.NewGuid(), revisedPayload, "Reviewer edit");
        _revisionRepoMock.Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default))
            .ReturnsAsync(revision);

        var columnRepoMock = new Mock<IColumnRepository>();
        columnRepoMock.Setup(r => r.GetByIdAsync(columnId, default))
            .ReturnsAsync(column);
        columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new[] { column });
        _unitOfWorkMock.Setup(u => u.Columns).Returns(columnRepoMock.Object);

        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(Array.Empty<Card>());
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: the diff describes the revised operation, not the original.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Revised card");
        result.Value.Should().NotContain("Original card");
        result.Value.Should().Contain("To Do");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReturnValidationError_WhenSavedRevisionPayloadIsInvalid()
    {
        // Arrange: a saved revision whose payload cannot be materialized into
        // operations. Apply would fail the same way, so the diff surfaces the failure
        // rather than silently falling back to the stale original preview (#1235).
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Create card",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        proposal.SetDiffPreview("0. Create card \"Original card\"");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Non-empty payload that satisfies the entity ctor but carries no operations
        // array — TryParseOperations rejects it (mirrors the executor's behavior).
        var revision = new ProposalRevision(proposalId, 1, Guid.NewGuid(), "{}", "Reviewer edit");
        _revisionRepoMock.Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default))
            .ReturnsAsync(revision);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectExpiredProposal_OnRevisedPath()
    {
        // #1376 asymmetry (1) on the revision-aware path: Apply materializes the latest
        // revision and runs ValidatePolicy (structure, then expiry) on it, so an expired
        // proposal with a saved revision is rejected at Apply. The revised diff path now
        // runs the same expiry gate, so preview == apply (400 "Proposal has expired")
        // instead of previewing the revised diff cleanly.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "To Do", 0);
        var columnId = column.Id;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Create card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        var revisedParams = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = "Revised card",
            columnId,
            boardId
        });
        var revisedPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType = "create",
                    targetType = "card",
                    parameters = revisedParams,
                    idempotencyKey = Guid.NewGuid().ToString()
                }
            }
        });
        var revision = new ProposalRevision(proposalId, 1, Guid.NewGuid(), revisedPayload, "Reviewer edit");
        _revisionRepoMock.Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default))
            .ReturnsAsync(revision);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: the revised diff is not rendered; the expiry rejection surfaces instead.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Proposal has expired");
    }

    // A non-expired, single-operation proposal used by the #1398 permission-parity tests
    // below. It passes the structure and expiry gates so execution reaches the permission
    // gate (requester exists → board exists → board access) that Apply runs via
    // AutomationPolicyEngine.ValidatePermissionsAsync.
    private static AutomationProposal BuildPermissionGateProposal(Guid requesterId, Guid boardId)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            requesterId,
            "Create card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { title = "Task", boardId });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", parameters, Guid.NewGuid().ToString()));

        return proposal;
    }

    // Mirrors BuildPermissionGateProposal's operation as the DTO list Apply feeds to
    // AutomationPolicyEngine.ValidatePermissionsAsync, so the same permission result can be
    // computed against the shared mocks and asserted identical to the diff-preview result.
    private static List<ProposalOperationDto> BuildPermissionGateApplyOperations(Guid proposalId, Guid boardId)
        => new()
        {
            new ProposalOperationDto(
                Guid.NewGuid(),
                proposalId,
                0,
                "create",
                "card",
                null,
                System.Text.Json.JsonSerializer.Serialize(new { title = "Task", boardId }),
                Guid.NewGuid().ToString(),
                null)
        };

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectRevokedBoardAccess_MatchingApplyPermissionGate()
    {
        // #1398 third preview/apply asymmetry: Apply runs ValidatePermissionsAsync after the
        // policy gate, so a proposal whose requester lost board access mid-review is rejected
        // 403 at Apply. The diff path never ran that gate, previewing a clean 200 and then
        // failing after approval. Preview now runs the same gate: preview == apply (403).
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        // Requester exists and the board exists (constructor defaults), but access is revoked.
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act (preview)
        var previewResult = await _service.GetProposalDiffAsync(proposalId);

        // Act (apply-side permission gate) on the equivalent operation DTOs.
        var applyResult = await new AutomationPolicyEngine(_unitOfWorkMock.Object).ValidatePermissionsAsync(
            requesterId, boardId, BuildPermissionGateApplyOperations(proposalId, boardId));

        // Assert: preview rejects, and rejects identically to Apply (403, same message).
        applyResult.IsSuccess.Should().BeFalse();
        applyResult.ErrorCode.Should().Be(ErrorCodes.Forbidden);

        previewResult.IsSuccess.Should().BeFalse();
        previewResult.ErrorCode.Should().Be(applyResult.ErrorCode);
        previewResult.ErrorMessage.Should().Be(applyResult.ErrorMessage);
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectDeletedBoard_MatchingApplyPermissionGate()
    {
        // #1398: a proposal whose board was deleted mid-review is rejected 404 at Apply
        // (ValidatePermissionsAsync board-existence gate). Preview now runs the same gate.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        // Board no longer exists (overrides the constructor default for this board id).
        _boardRepoMock
            .Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board?)null);

        var previewResult = await _service.GetProposalDiffAsync(proposalId);
        var applyResult = await new AutomationPolicyEngine(_unitOfWorkMock.Object).ValidatePermissionsAsync(
            requesterId, boardId, BuildPermissionGateApplyOperations(proposalId, boardId));

        applyResult.IsSuccess.Should().BeFalse();
        applyResult.ErrorCode.Should().Be(ErrorCodes.NotFound);

        previewResult.IsSuccess.Should().BeFalse();
        previewResult.ErrorCode.Should().Be(applyResult.ErrorCode);
        previewResult.ErrorMessage.Should().Be(applyResult.ErrorMessage);
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectDeletedRequester_MatchingApplyPermissionGate()
    {
        // #1398: a proposal whose requester (user) was deleted mid-review is rejected 404 at
        // Apply (ValidatePermissionsAsync user-existence gate). Preview now runs the same gate.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        // Requester no longer exists (overrides the constructor default for this user id).
        _userRepoMock
            .Setup(r => r.GetByIdAsync(requesterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var previewResult = await _service.GetProposalDiffAsync(proposalId);
        var applyResult = await new AutomationPolicyEngine(_unitOfWorkMock.Object).ValidatePermissionsAsync(
            requesterId, boardId, BuildPermissionGateApplyOperations(proposalId, boardId));

        applyResult.IsSuccess.Should().BeFalse();
        applyResult.ErrorCode.Should().Be(ErrorCodes.NotFound);

        previewResult.IsSuccess.Should().BeFalse();
        previewResult.ErrorCode.Should().Be(applyResult.ErrorCode);
        previewResult.ErrorMessage.Should().Be(applyResult.ErrorMessage);
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldEnforcePermissionGate_EvenWithStoredPreview()
    {
        // #1398, cached-preview fast path (the #1376 lesson): a proposal carrying a stored
        // DiffPreview whose requester lost board access previously previewed 200 with that
        // stale preview, then failed Apply 403. The permission gate now runs BEFORE the cached
        // preview return, so preview == apply (403) and the stored preview is NOT surfaced.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);
        proposal.SetDiffPreview("0. Create card \"Task\"");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert: the cached preview is NOT returned; the permission rejection is.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldEnforcePermissionGate_OnRevisedPath()
    {
        // #1398 on the revision-aware path: Apply materializes the latest revision and still
        // runs ValidatePermissionsAsync (requester board access). A revised proposal whose
        // requester lost access is rejected 403 at Apply; the revised diff path now runs the
        // same gate rather than rendering the revised diff cleanly.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            requesterId,
            "Create card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);

        var revisedParams = System.Text.Json.JsonSerializer.Serialize(new { title = "Revised card", boardId });
        var revisedPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType = "create",
                    targetType = "card",
                    parameters = revisedParams,
                    idempotencyKey = Guid.NewGuid().ToString()
                }
            }
        });
        var revision = new ProposalRevision(proposalId, 1, Guid.NewGuid(), revisedPayload, "Reviewer edit");
        _revisionRepoMock.Setup(r => r.GetLatestByProposalIdAsync(proposal.Id, default)).ReturnsAsync(revision);

        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.GetProposalDiffAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    // The REAL Apply trust boundary over the same shared mocks: a concrete
    // AutomationExecutorService wired with the real proposal service under test and a real
    // AutomationPolicyEngine (no mocked gates). Parity tests drive this instead of calling
    // the engine method directly, so they fail if the executor's gate sequence ever diverges
    // from what the diff path mirrors — not just if the shared engine changes (#1413 LOW-3).
    private AutomationExecutorService BuildRealApplyExecutor()
        => new(
            _unitOfWorkMock.Object,
            _service,
            new AutomationPolicyEngine(_unitOfWorkMock.Object),
            new CardService(_unitOfWorkMock.Object),
            new BoardService(_unitOfWorkMock.Object),
            new ColumnService(_unitOfWorkMock.Object));

    [Fact]
    public async Task GetProposalDiffAsync_ShouldMatchRealApplyExecutor_WhenBoardAccessRevoked()
    {
        // #1413 LOW-3: the ...MatchingApplyPermissionGate tests above compare preview against
        // the same engine method the service delegates to, so they cannot detect the REAL
        // Apply path diverging. This test drives AutomationExecutorService.ExecuteProposalAsync
        // end-to-end on an APPROVED, non-terminal, non-expired proposal over the same fixture
        // state (requester board access revoked) and asserts the diff rejection is
        // code-and-message identical to the executor's rejection.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);
        proposal.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act: preview, then the real Apply boundary.
        var previewResult = await _service.GetProposalDiffAsync(proposalId);
        var applyResult = await BuildRealApplyExecutor()
            .ExecuteProposalAsync(proposalId, Guid.NewGuid().ToString());

        // Assert: the real executor rejects 403 at its permission gate, and preview rejects
        // identically — code AND message.
        applyResult.IsSuccess.Should().BeFalse();
        applyResult.ErrorCode.Should().Be(ErrorCodes.Forbidden);

        previewResult.IsSuccess.Should().BeFalse();
        previewResult.ErrorCode.Should().Be(applyResult.ErrorCode);
        previewResult.ErrorMessage.Should().Be(applyResult.ErrorMessage);
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldRejectRevokedAccess_ForTerminalAppliedProposal()
    {
        // #1413 LOW-1 behavior-change pin: for a TERMINAL Applied proposal whose requester
        // later lost board access, diff previously returned the stored preview (200); it now
        // returns 403. This is INTENDED: Apply short-circuits terminal status to idempotent
        // success BEFORE its permission gate, so preview is deliberately stricter than Apply's
        // terminal no-op — a revoked reviewer can no longer read board contents through a
        // terminal proposal's diff. Coherent with the #1397 maintainer decision (frontend
        // stops firing live diffs for terminal items). Both sides are pinned here so any
        // future drift in either direction fails this test.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);
        proposal.SetDiffPreview("0. Create card \"Task\"");
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var previewResult = await _service.GetProposalDiffAsync(proposalId);
        var applyResult = await BuildRealApplyExecutor()
            .ExecuteProposalAsync(proposalId, Guid.NewGuid().ToString());

        // Assert: preview rejects 403 (stored preview NOT surfaced) while the real Apply
        // boundary short-circuits the already-applied proposal to idempotent success.
        previewResult.IsSuccess.Should().BeFalse();
        previewResult.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        previewResult.Value.Should().BeNull();

        applyResult.IsSuccess.Should().BeTrue(applyResult.ErrorMessage);
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReportExpiry_NotForbidden_WhenExpiredAndAccessRevoked()
    {
        // #1413 LOW-4 gate-ordering pin: a proposal that is BOTH expired AND has revoked
        // requester access must fail with the expiry ValidationError — not Forbidden — from
        // BOTH trust boundaries, because both run structure → expiry (ValidatePolicy order)
        // BEFORE the permission gate. If either side ever reorders permissions ahead of
        // structure/expiry, its error flips to Forbidden and this test fails on that side.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildPermissionGateProposal(requesterId, boardId);
        // Approve while still valid (the domain forbids approving an expired proposal),
        // then force expiry — mirrors a proposal that expired after approval.
        proposal.Approve(Guid.NewGuid());
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var previewResult = await _service.GetProposalDiffAsync(proposalId);
        var applyResult = await BuildRealApplyExecutor()
            .ExecuteProposalAsync(proposalId, Guid.NewGuid().ToString());

        // Assert: BOTH sides report the expiry ValidationError, never Forbidden.
        previewResult.IsSuccess.Should().BeFalse();
        previewResult.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        previewResult.ErrorMessage.Should().Be("Proposal has expired");

        applyResult.IsSuccess.Should().BeFalse();
        applyResult.ErrorCode.Should().Be(previewResult.ErrorCode);
        applyResult.ErrorMessage.Should().Be(previewResult.ErrorMessage);
    }

    #endregion

    #region GetTerminalProposalStoredPreviewAsync Tests (#1415)

    private static AutomationProposal BuildTerminalPreviewProposal(Guid requesterId, Guid boardId, string preview)
    {
        // A board-scoped Applied proposal carrying one benign update-board operation. The terminal
        // stored-preview read gates ONLY on requester/board access (ValidateBoardAccessAsync) —
        // operations are never re-validated against live board state — so the op shape here is
        // representative rather than load-bearing.
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            requesterId,
            "Rename board",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { name = "Renamed board", boardId });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "board", parameters, Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));

        // SetDiffPreview requires PendingReview, so stamp the stored preview before deciding.
        proposal.SetDiffPreview(preview);
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();
        return proposal;
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnStoredPreview_WhenRequesterRetainsAccess()
    {
        // #1415: a decided proposal whose requester still has board access serves the STORED
        // historical preview verbatim (no live rebuild), mirroring the #1397 frontend decision.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildTerminalPreviewProposal(requesterId, boardId, "0. Create card \"Task\"");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("0. Create card \"Task\"");
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnForbidden_WhenRequesterLostBoardAccess()
    {
        // #1415: the core trust-class fix. A requester who lost board access must be denied the
        // stored preview with the SAME Forbidden the diff/apply permission gate returns — the
        // stored preview is NOT surfaced.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildTerminalPreviewProposal(requesterId, boardId, "leaked-preview");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnNotFound_WhenBoardDeleted()
    {
        // #1415 board-exists gate: a decided proposal whose board was deleted returns 404, never
        // the stored preview.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildTerminalPreviewProposal(requesterId, boardId, "orphan-preview");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardRepoMock
            .Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board?)null);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnNotFound_WhenRequesterDeleted()
    {
        // #1415 requester-exists gate: a decided proposal whose requester was deleted returns 404.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildTerminalPreviewProposal(requesterId, boardId, "ghost-preview");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _userRepoMock
            .Setup(r => r.GetByIdAsync(requesterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnNotFound_WhenProposalMissing()
    {
        var proposalId = Guid.NewGuid();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync((AutomationProposal?)null);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldSkipExpiryGate_ServingStoredPreview_WhenExpiredButAccessRetained()
    {
        // #1415 deliberate divergence from the live diff path: the pre-decision structure/expiry
        // gates no longer apply once a proposal is decided. An Applied proposal whose ExpiresAt has
        // since passed still serves its stored historical preview (subject to board access), where
        // GetProposalDiffAsync would report expiry. Only the requester/board-access gate is enforced.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildTerminalPreviewProposal(requesterId, boardId, "expired-but-historical");
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("expired-but-historical");
    }

    private static AutomationProposal BuildZeroOperationTerminalProposal(Guid requesterId, Guid boardId, string preview)
    {
        // CreateProposalAsync enforces no minimum operation count, so a board-scoped proposal can
        // carry zero operations and still be decided (here: Rejected). ValidatePermissionsAsync's
        // empty-operations short-circuit skips its board half for this shape — the terminal read
        // calls ValidateBoardAccessAsync directly so the board gate holds uniformly.
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            requesterId,
            "Zero-op proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        proposal.SetDiffPreview(preview);
        proposal.Reject(Guid.NewGuid(), "not needed");
        return proposal;
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnForbidden_WhenZeroOpBoardScopedProposalLostAccess()
    {
        // #1415 regression guard: a board-scoped decided proposal with NO operations must still be
        // denied to a requester who lost board access — ValidatePermissionsAsync short-circuits on
        // the empty op list before its board half, so the terminal read's direct
        // ValidateBoardAccessAsync call must fail it closed.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildZeroOperationTerminalProposal(requesterId, boardId, "leaked-empty-preview");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, requesterId, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnNotFound_WhenZeroOpBoardScopedProposalBoardDeleted()
    {
        // #1415 regression guard: the board-exists half of the gate must also run for a zero-op
        // board-scoped decided proposal.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildZeroOperationTerminalProposal(requesterId, boardId, "orphan-empty-preview");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _boardRepoMock
            .Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board?)null);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnStoredPreview_WhenZeroOpBoardScopedProposalRetainsAccess()
    {
        // The guard denies only revoked/deleted access — a zero-op board-scoped proposal whose
        // requester still has access serves its stored preview normally.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = BuildZeroOperationTerminalProposal(requesterId, boardId, "empty-but-visible");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("empty-but-visible");
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldServeStoredPreview_WhenAppliedMoveCardReferencesDeletedCard()
    {
        // #1425 MEDIUM regression pin (over-gating): an Applied move-card proposal whose referenced
        // card was deleted AFTER apply must still serve its stored historical preview to a requester
        // with intact access. The terminal read must NOT run the operation-contract validator — that
        // validator checks references against LIVE board state (ValidateCardBoardAsync) and would
        // wrongly deny the historical preview with a misleading scope/NotFound error.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var cardId = Guid.NewGuid();

        var proposal = new AutomationProposal(
            ProposalSourceType.Chat, requesterId, "Move card", RiskLevel.Medium,
            Guid.NewGuid().ToString(), boardId);
        var parameters = System.Text.Json.JsonSerializer.Serialize(new { boardId, cardId, columnId = Guid.NewGuid() });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "move", "card", parameters, Guid.NewGuid().ToString(),
            targetId: cardId.ToString()));
        proposal.SetDiffPreview("historical: moved card to Done");
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        // The referenced card no longer exists (deleted post-apply).
        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>())).ReturnsAsync((Card?)null);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be("historical: moved card to Done");
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldServeStoredPreview_WhenAppliedCreateCardTargetIdNowResolves()
    {
        // #1425 MEDIUM regression pin (the always-fires case): an Applied create-card proposal's
        // TargetId resolves to the card Apply created — the operation-contract validator's
        // new-card-id collision check (ValidateNewCardIdAsync) would ALWAYS reject it with
        // Conflict. The terminal read must serve the stored preview immediately instead.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var createdCardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();

        var proposal = new AutomationProposal(
            ProposalSourceType.Chat, requesterId, "Create card", RiskLevel.Low,
            Guid.NewGuid().ToString(), boardId);
        var parameters = System.Text.Json.JsonSerializer.Serialize(new { boardId, title = "Task", columnId });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", parameters, Guid.NewGuid().ToString(),
            targetId: createdCardId.ToString()));
        proposal.SetDiffPreview("historical: created card \"Task\"");
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        // The created card EXISTS now — exactly what the live new-card-id check would reject.
        var cardRepoMock = new Mock<ICardRepository>();
        cardRepoMock
            .Setup(r => r.GetByIdAsync(createdCardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestDataBuilder.CreateCard(boardId, columnId, "Task"));
        _unitOfWorkMock.Setup(u => u.Cards).Returns(cardRepoMock.Object);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be("historical: created card \"Task\"");
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnNullPreview_WhenNoPreviewWasStored()
    {
        // #1425 LOW-2 pin: a decided proposal that never had a preview stored returns null (not ""),
        // so MCP clients can distinguish never-stored from stored-but-empty — matching how the raw
        // field serialized before the gating.
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat, requesterId, "Never previewed", RiskLevel.Low,
            Guid.NewGuid().ToString(), boardId);
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetTerminalProposalStoredPreviewAsync_ShouldReturnNullPreview_WhenPinnedRevisionApplies()
    {
        // #1439: an Applied proposal that pinned a revision at approve time must NOT serve its
        // stored DiffPreview — that preview describes the ORIGINAL operations, while proposal_detail
        // also carries the revision-derived operation set, so serving both would let one MCP payload
        // present two disagreeing views of the same change. When an effective revision applies the
        // preview is suppressed (null).
        var proposalId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();

        var proposal = new AutomationProposal(
            ProposalSourceType.Chat, requesterId, "Rename board", RiskLevel.Low,
            Guid.NewGuid().ToString(), boardId);
        var parameters = System.Text.Json.JsonSerializer.Serialize(new { name = "Renamed board", boardId });
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "board", parameters, Guid.NewGuid().ToString(),
            targetId: boardId.ToString()));
        proposal.SetDiffPreview("historical: original operations");
        proposal.Approve(Guid.NewGuid(), revisionId); // pins ApprovedRevisionId
        proposal.MarkAsApplied();

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        var revision = new ProposalRevision(proposal.Id, 1, Guid.NewGuid(), "{}", "Reviewer edit");
        _revisionRepoMock.Setup(r => r.GetByIdAsync(revisionId, default)).ReturnsAsync(revision);

        var result = await _service.GetTerminalProposalStoredPreviewAsync(proposalId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().BeNull(
            "a pinned-revision terminal proposal suppresses its original-operations stored preview");
    }

    #endregion

    #region GetEffectiveRevision freeze for rejected proposals (#1439)

    // Builds an already-Rejected proposal carrying one original "update board" operation plus a
    // saved revision, with DecidedAt and the revision's RevisedAt forced to fixed values so the
    // decision-time cutoff is deterministic.
    private (AutomationProposal Proposal, ProposalRevision Revision) BuildRejectedProposalWithRevision(
        Guid proposalId,
        DateTime decidedAt,
        DateTimeOffset revisionRevisedAt)
    {
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat, Guid.NewGuid(), "Rejected proposal", RiskLevel.Low,
            Guid.NewGuid().ToString(), boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "board",
            System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Original name" }),
            Guid.NewGuid().ToString(), targetId: boardId.ToString()));
        proposal.Reject(Guid.NewGuid(), "not needed");
        SetDecidedAt(proposal, decidedAt);

        var revisedPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType = "update",
                    targetType = "board",
                    targetId = boardId.ToString(),
                    parameters = System.Text.Json.JsonSerializer.Serialize(new { boardId, name = "Revised name" }),
                    idempotencyKey = Guid.NewGuid().ToString()
                }
            }
        });
        var revision = new ProposalRevision(proposal.Id, 1, Guid.NewGuid(), revisedPayload, "Reviewer edit");
        SetRevisedAt(revision, revisionRevisedAt);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default)).ReturnsAsync(proposal);
        _revisionRepoMock.Setup(r => r.GetByProposalIdAsync(proposal.Id, default))
            .ReturnsAsync(new[] { revision });
        return (proposal, revision);
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldShowPreDecisionRevision_ForRejectedProposal()
    {
        // #1439: a rejected proposal is frozen at its decision time. A revision saved AT OR BEFORE
        // DecidedAt is what the reviewer decided on, so the GET response surfaces its revised ops.
        var proposalId = Guid.NewGuid();
        var decision = new DateTime(2026, 7, 18, 13, 0, 0, DateTimeKind.Utc);
        BuildRejectedProposalWithRevision(proposalId, decision, new DateTimeOffset(decision.AddHours(-1), TimeSpan.Zero));

        var result = await _service.GetProposalByIdAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().ContainSingle();
        result.Value.Operations[0].Parameters.Should().Contain("Revised name");
        result.Value.Operations[0].Parameters.Should().NotContain("Original name");
    }

    [Fact]
    public async Task GetProposalByIdAsync_ShouldShowOriginalOperations_ForRejectedProposalWithOnlyPostDecisionRevision()
    {
        // #1439: a revision that raced in AFTER rejection must never surface — the frozen response
        // falls back to the original operations the reviewer actually rejected.
        var proposalId = Guid.NewGuid();
        var decision = new DateTime(2026, 7, 18, 13, 0, 0, DateTimeKind.Utc);
        BuildRejectedProposalWithRevision(proposalId, decision, new DateTimeOffset(decision.AddHours(1), TimeSpan.Zero));

        var result = await _service.GetProposalByIdAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().ContainSingle();
        result.Value.Operations[0].Parameters.Should().Contain("Original name");
        result.Value.Operations[0].Parameters.Should().NotContain("Revised name");
    }

    #endregion

    #region GetProposalsAsync Tests

    [Fact]
    public async Task GetProposalsAsync_ShouldFilterByStatus_WhenStatusProvided()
    {
        // Arrange
        var proposals = new[]
        {
            new AutomationProposal(ProposalSourceType.Chat, Guid.NewGuid(), "Test 1", RiskLevel.Low, Guid.NewGuid().ToString()),
            new AutomationProposal(ProposalSourceType.Chat, Guid.NewGuid(), "Test 2", RiskLevel.Low, Guid.NewGuid().ToString())
        };

        _proposalRepoMock.Setup(r => r.GetByStatusAsync(ProposalStatus.PendingReview, 100, default))
            .ReturnsAsync(proposals);

        // Act
        var result = await _service.GetProposalsAsync(new ProposalFilterDto(Status: ProposalStatus.PendingReview));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetProposalsAsync_ShouldFilterByBoardId_WhenProvided()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var proposals = new[]
        {
            new AutomationProposal(ProposalSourceType.Chat, Guid.NewGuid(), "Test", RiskLevel.Low, Guid.NewGuid().ToString(), boardId)
        };

        _proposalRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, 100, default))
            .ReturnsAsync(proposals);

        // Act
        var result = await _service.GetProposalsAsync(new ProposalFilterDto(BoardId: boardId));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetProposalsAsync_ShouldQueryByUserFirst_WhenUserAndStatusFiltersProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var pending = new AutomationProposal(ProposalSourceType.Chat, userId, "Pending", RiskLevel.Low, Guid.NewGuid().ToString());
        var approved = new AutomationProposal(ProposalSourceType.Chat, userId, "Approved", RiskLevel.Low, Guid.NewGuid().ToString());
        approved.Approve(Guid.NewGuid());

        _proposalRepoMock.Setup(r => r.GetByUserIdAsync(userId, 10, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pending, approved });

        // Act
        var result = await _service.GetProposalsAsync(new ProposalFilterDto(Status: ProposalStatus.PendingReview, UserId: userId, Limit: 10));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(p => p.Id == pending.Id);
        _proposalRepoMock.Verify(r => r.GetByUserIdAsync(userId, 10, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        _proposalRepoMock.Verify(r => r.GetByStatusAsync(It.IsAny<ProposalStatus>(), It.IsAny<int>(), default), Times.Never);
    }

    #endregion
}
