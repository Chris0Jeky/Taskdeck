using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public class ExecutionAuditRecorderTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly ExecutionAuditRecorder _recorder;

    public ExecutionAuditRecorderTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);

        _recorder = new ExecutionAuditRecorder(_unitOfWorkMock.Object);
    }

    [Theory]
    [InlineData("create", AuditAction.Created)]
    [InlineData("update", AuditAction.Updated)]
    [InlineData("archive", AuditAction.Archived)]
    [InlineData("move", AuditAction.Moved)]
    [InlineData("reorder", AuditAction.Moved)]
    public async Task RecordAsync_ShouldMapActionTypesToCorrectAuditAction(string actionType, AuditAction expectedAction)
    {
        var boardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, actionType, "board", null,
            $$"""{"boardId":"{{boardId}}"}""", "key1", null);
        var proposal = CreateProposal(boardId: boardId);

        await _recorder.RecordAsync(operation, proposal, default);

        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == expectedAction),
            default), Times.Once);
    }

    [Theory]
    [InlineData("archive-lifecycle", AuditAction.Archived, "Card archived; original placement retained")]
    [InlineData("restore-lifecycle", AuditAction.Unarchived, "Card restored to original column")]
    [InlineData("ARCHIVE-LIFECYCLE", AuditAction.Archived, "Card archived; original placement retained")]
    [InlineData("Restore-Lifecycle", AuditAction.Unarchived, "Card restored to original column")]
    public async Task RecordAsync_ShouldWriteTypedLifecycleReceiptWithProposalProvenance(
        string actionType, AuditAction expectedAction, string expectedSummary)
    {
        // #2939: the proposal lane suppresses CardService's own lifecycle receipt, so this single
        // entry must be correctly typed AND carry the proposal provenance - never an "Updated" fallback.
        var cardId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), proposalId, 2, actionType, "card", cardId.ToString(),
            $$"""{"cardId":"{{cardId}}"}""", "key1", null);
        var proposal = CreateProposal(proposalId: proposalId, requestedByUserId: requestedBy);

        await _recorder.RecordAsync(operation, proposal, default);

        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a =>
                a.Action == expectedAction &&
                a.EntityType == "card" &&
                a.EntityId == cardId &&
                a.UserId == requestedBy &&
                a.Changes != null &&
                a.Changes.StartsWith(expectedSummary) &&
                a.Changes.Contains(proposalId.ToString()) &&
                a.Changes.Contains("sequence 2")),
            default), Times.Once);
        _auditLogRepoMock.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), default), Times.Once);
    }

    [Fact]
    public async Task RecordAsync_ShouldDefaultToUpdated_ForUnknownActionType()
    {
        var boardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "custom", "board", null,
            $$"""{"boardId":"{{boardId}}"}""", "key1", null);
        var proposal = CreateProposal(boardId: boardId);

        await _recorder.RecordAsync(operation, proposal, default);

        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == AuditAction.Updated),
            default), Times.Once);
    }

    [Fact]
    public async Task RecordAsync_ShouldUseTargetIdAsEntityId_WhenProvided()
    {
        var targetId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", targetId.ToString(),
            """{"title":"Test"}""", "key1", null);
        var proposal = CreateProposal();

        await _recorder.RecordAsync(operation, proposal, default);

        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.EntityType == "card" && a.EntityId == targetId),
            default), Times.Once);
    }

    [Fact]
    public async Task RecordAsync_ShouldAttributeActorToApplyingUser_AndKeepRequesterInProvenance()
    {
        // #2978: when editor B applies a proposal authored by A, the handler's own mutation rows
        // name B. The execution-history row used to name A, so one board change showed two rows
        // with contradictory actors. The actor is now the applier; A survives as provenance text.
        var requester = Guid.NewGuid();
        var applier = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), proposalId, 4, "replace-assignments", "card", cardId.ToString(),
            $$"""{"cardId":"{{cardId}}","userIds":[]}""", "key1", null);
        var proposal = CreateProposal(proposalId: proposalId, requestedByUserId: requester);

        await _recorder.RecordAsync(operation, proposal, default, actorUserId: applier);

        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a =>
                a.UserId == applier &&
                a.Changes != null &&
                a.Changes.Contains(requester.ToString()) &&
                a.Changes.Contains(proposalId.ToString())),
            default), Times.Once);
    }

    [Theory]
    [InlineData("archive-lifecycle", AuditAction.Archived)]
    [InlineData("restore-lifecycle", AuditAction.Unarchived)]
    public async Task RecordAsync_ShouldAttributeLifecycleReceiptToApplyingUser(string actionType, AuditAction expectedAction)
    {
        // #2978 + #2939: the lifecycle receipt is the ONLY row for a proposal-applied
        // archive/restore, so if it named the requester the applier would be invisible.
        var requester = Guid.NewGuid();
        var applier = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, actionType, "card", cardId.ToString(),
            $$"""{"cardId":"{{cardId}}"}""", "key1", null);
        var proposal = CreateProposal(requestedByUserId: requester);

        await _recorder.RecordAsync(operation, proposal, default, actorUserId: applier);

        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a =>
                a.Action == expectedAction &&
                a.UserId == applier &&
                a.Changes != null &&
                a.Changes.Contains(requester.ToString())),
            default), Times.Once);
    }

    [Fact]
    public async Task RecordAsync_ShouldFallBackToRequester_WhenNoAuthenticatedApplier()
    {
        // Internal lanes execute without an authenticated caller; the requester stays the best
        // available actor there, which is also the pre-#2978 behaviour for same-user applies.
        var requester = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "card", Guid.NewGuid().ToString(),
            """{"title":"Test"}""", "key1", null);
        var proposal = CreateProposal(requestedByUserId: requester);

        await _recorder.RecordAsync(operation, proposal, default, actorUserId: null);

        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.UserId == requester), default), Times.Once);
    }

    [Fact]
    public async Task RecordAsync_ShouldFallBackToRequester_WhenApplierIdIsEmpty()
    {
        // AuditLog rejects Guid.Empty, and this runs inside the execution transaction - an empty
        // actor must degrade to the requester rather than throw and roll the apply back.
        var requester = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "card", Guid.NewGuid().ToString(),
            """{"title":"Test"}""", "key1", null);
        var proposal = CreateProposal(requestedByUserId: requester);

        await _recorder.RecordAsync(operation, proposal, default, actorUserId: Guid.Empty);

        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.UserId == requester), default), Times.Once);
    }

    #region ResolveAuditEntity

    [Fact]
    public void ResolveAuditEntity_ShouldFallBackToCardId_WhenNoTargetId()
    {
        var cardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "card", null,
            $$"""{"cardId":"{{cardId}}"}""", "key1", null);
        var proposal = CreateProposal();

        var (entityType, entityId) = ExecutionAuditRecorder.ResolveAuditEntity(operation, proposal);

        entityType.Should().Be("card");
        entityId.Should().Be(cardId);
    }

    [Fact]
    public void ResolveAuditEntity_ShouldFallBackToColumnId_WhenNoCardId()
    {
        var columnId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "reorder", "column", null,
            $$"""{"columnId":"{{columnId}}","position":0}""", "key1", null);
        var proposal = CreateProposal();

        var (entityType, entityId) = ExecutionAuditRecorder.ResolveAuditEntity(operation, proposal);

        entityType.Should().Be("column");
        entityId.Should().Be(columnId);
    }

    [Fact]
    public void ResolveAuditEntity_ShouldFallBackToBoardId_WhenNoColumnOrCardId()
    {
        var boardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", null,
            $$"""{"boardId":"{{boardId}}","name":"New"}""", "key1", null);
        var proposal = CreateProposal();

        var (entityType, entityId) = ExecutionAuditRecorder.ResolveAuditEntity(operation, proposal);

        entityType.Should().Be("board");
        entityId.Should().Be(boardId);
    }

    [Fact]
    public void ResolveAuditEntity_ShouldFallBackToProposalBoardId_WhenNoParameterIds()
    {
        var boardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", null,
            """{"name":"New"}""", "key1", null);
        var proposal = CreateProposal(boardId: boardId);

        var (entityType, entityId) = ExecutionAuditRecorder.ResolveAuditEntity(operation, proposal);

        entityType.Should().Be("board");
        entityId.Should().Be(boardId);
    }

    [Fact]
    public void ResolveAuditEntity_ShouldFallBackToProposalId_WhenNothingElseAvailable()
    {
        var proposalId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), proposalId, 0, "custom", "custom", null,
            """{"unknown":"value"}""", "key1", null);
        var proposal = CreateProposal(proposalId: proposalId, boardId: null);

        var (entityType, entityId) = ExecutionAuditRecorder.ResolveAuditEntity(operation, proposal);

        entityType.Should().Be("automation-proposal");
        entityId.Should().Be(proposalId);
    }

    #endregion

    #region BuildAuditChanges

    [Fact]
    public void BuildAuditChanges_ShouldIncludeProposalIdAndSequence()
    {
        var proposalId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), proposalId, 3, "update", "card", null,
            """{"cardId":"abc"}""", "key1", null);
        var proposal = CreateProposal(proposalId: proposalId);

        var changes = ExecutionAuditRecorder.BuildAuditChanges(operation, proposal);

        changes.Should().Contain(proposalId.ToString());
        changes.Should().Contain("sequence 3");
        changes.Should().Contain("update card");
    }

    [Fact]
    public void BuildAuditChanges_ShouldNameTheRequester()
    {
        // #2978: the row's actor is the applier, so the requester is only discoverable here.
        var requester = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 1, "update", "card", null,
            """{"cardId":"abc"}""", "key1", null);
        var proposal = CreateProposal(requestedByUserId: requester);

        var changes = ExecutionAuditRecorder.BuildAuditChanges(operation, proposal);

        changes.Should().Contain($"requested by user {requester}");
    }

    [Fact]
    public void BuildAuditChanges_ShouldKeepLegacyLifecycleWording_AheadOfProvenance()
    {
        var proposalId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), proposalId, 0, "archive-lifecycle", "card", null,
            """{"cardId":"abc"}""", "key1", null);
        var proposal = CreateProposal(proposalId: proposalId);

        var changes = ExecutionAuditRecorder.BuildAuditChanges(operation, proposal);

        changes.Should().StartWith("Card archived; original placement retained. ");
        changes.Should().Contain($"Automation proposal {proposalId}");
    }

    [Fact]
    public void BuildAuditChanges_ShouldNotPrefixLifecycleWording_ForNonLifecycleActions()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "archive", "card", null,
            """{"cardId":"abc"}""", "key1", null);
        var proposal = CreateProposal();

        var changes = ExecutionAuditRecorder.BuildAuditChanges(operation, proposal);

        changes.Should().StartWith("Automation proposal ");
    }

    [Fact]
    public void BuildAuditChanges_ShouldTruncateLongParameters()
    {
        var longParams = new string('x', 600);
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "card", null,
            longParams, "key1", null);
        var proposal = CreateProposal();

        var changes = ExecutionAuditRecorder.BuildAuditChanges(operation, proposal);

        changes.Should().EndWith("...");
        changes.Length.Should().BeLessThan(longParams.Length + 200);
    }

    #endregion

    private static ProposalDto CreateProposal(Guid? proposalId = null, Guid? boardId = null, Guid? requestedByUserId = null)
    {
        return new ProposalDto(
            proposalId ?? Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            requestedByUserId ?? Guid.NewGuid(),
            ProposalStatus.Approved,
            RiskLevel.Low,
            "Test",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            null,
            null,
            null,
            null,
            "corr1",
            new List<ProposalOperationDto>());
    }
}
