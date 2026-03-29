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

    private static ProposalDto CreateProposal(Guid? proposalId = null, Guid? boardId = null)
    {
        return new ProposalDto(
            proposalId ?? Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            Guid.NewGuid(),
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
