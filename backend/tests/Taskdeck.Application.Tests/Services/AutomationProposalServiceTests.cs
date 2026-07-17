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

public class AutomationProposalServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IProposalProvenanceRepository> _provenanceRepoMock;
    private readonly Mock<IProposalRevisionRepository> _revisionRepoMock;
    private readonly AutomationProposalService _service;

    public AutomationProposalServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _notificationServiceMock = new Mock<INotificationService>();
        _provenanceRepoMock = new Mock<IProposalProvenanceRepository>();
        _revisionRepoMock = new Mock<IProposalRevisionRepository>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ProposalRevisions).Returns(_revisionRepoMock.Object);
        // Default: no saved revision, so GetProposalDiffAsync uses the original path.
        // The revision-aware test overrides this per-proposal.
        _revisionRepoMock
            .Setup(r => r.GetLatestByProposalIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((ProposalRevision?)null);
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));

        _service = new AutomationProposalService(
            _unitOfWorkMock.Object,
            _notificationServiceMock.Object,
            _provenanceRepoMock.Object);
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
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

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
    public async Task GetProposalDiffAsync_ShouldReturnDiff_WhenAvailable()
    {
        // Arrange
        var proposalId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        proposal.SetDiffPreview("+ New card created\n- Old card removed");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("New card created");
    }

    [Fact]
    public async Task GetProposalDiffAsync_ShouldReturnNotFound_WhenDiffNotAvailable()
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
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
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
        // Arrange: a 3-column board so the requested destination (position 2) is in range.
        // An in-range reorder previews the exact position Apply lands on. (Previously this
        // test used a single-column board with position 2 — an out-of-range target that
        // Apply clamps to the end — and asserted the raw requested value, locking in the
        // preview != apply divergence this issue fixes. See the clamp-specific test below.)
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

        var parameters = System.Text.Json.JsonSerializer.Serialize(new { columnId, position = 2 });
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

        // Assert: the approval preview names the column and its destination position.
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Contain("Reorder");
        result.Value.Should().Contain("In Progress");
        result.Value.Should().Contain("to position 2");
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
        _revisionRepoMock.Setup(r => r.GetLatestByProposalIdAsync(proposalId, default))
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
        _revisionRepoMock.Setup(r => r.GetLatestByProposalIdAsync(proposalId, default))
            .ReturnsAsync(revision);

        // Act
        var result = await _service.GetProposalDiffAsync(proposalId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
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
