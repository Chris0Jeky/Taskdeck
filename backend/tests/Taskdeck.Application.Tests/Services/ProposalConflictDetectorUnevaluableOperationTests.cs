using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class ProposalConflictDetectorUnevaluableOperationTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _cardId = Guid.NewGuid();
    private readonly Guid _sourceColumnId = Guid.NewGuid();
    private readonly Guid _targetColumnId = Guid.NewGuid();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAutomationProposalRepository> _proposals = new();
    private readonly Mock<ICardRepository> _cards = new();
    private readonly Mock<IColumnRepository> _columns = new();
    private readonly Mock<ICardCommentRepository> _comments = new();
    private readonly Mock<IOutboundWebhookSubscriptionRepository> _webhooks = new();
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly Mock<IRelatedProposalEvidenceService> _relatedEvidence = new();
    private readonly RecordingLogger<ProposalConflictDetector> _logger = new();
    private readonly ProposalConflictDetector _detector;

    public ProposalConflictDetectorUnevaluableOperationTests()
    {
        _unitOfWork.SetupGet(unit => unit.AutomationProposals).Returns(_proposals.Object);
        _unitOfWork.SetupGet(unit => unit.Cards).Returns(_cards.Object);
        _unitOfWork.SetupGet(unit => unit.Columns).Returns(_columns.Object);
        _unitOfWork.SetupGet(unit => unit.CardComments).Returns(_comments.Object);
        _unitOfWork.SetupGet(unit => unit.OutboundWebhookSubscriptions).Returns(_webhooks.Object);

        var card = new Card(_cardId, _boardId, _sourceColumnId, "Review target");
        _cards.Setup(repository => repository.GetByIdAsync(_cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        _columns.Setup(repository => repository.GetByIdWithCardsAsync(_targetColumnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Column(_boardId, "Target", 1, wipLimit: 5));
        _relatedEvidence.Setup(service => service.HasOtherPendingProposalTargetingCardAsync(
                It.IsAny<ProposalEvidenceScope>(), It.IsAny<Guid>(), _cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _comments.Setup(repository => repository.CountByCardIdAsync(_cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _webhooks.Setup(repository => repository.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());
        _authorization.Setup(service => service.CanReadBoardAsync(_userId, _boardId))
            .ReturnsAsync(Result.Success(true));

        _detector = new ProposalConflictDetector(
            _unitOfWork.Object,
            _authorization.Object,
            _relatedEvidence.Object,
            _logger);
    }

    [Theory]
    [InlineData("not-json{")]
    [InlineData("[]")]
    [InlineData("{\"columnId\":12345}")]
    [InlineData("{\"destinationColumnId\":\"72fd92f9-4253-4f38-8cda-2110467ba941\"}")]
    public async Task DetectConflictsAsync_UnevaluableMove_SurfacesIncompleteReview(string parameters)
    {
        var proposal = CreateProposal(
            RiskLevel.Low,
            CreateMoveOperation(Guid.NewGuid(), 0, parameters));

        var result = await _detector.DetectConflictsAsync(proposal, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Warn
            && row.Key == "unable-to-evaluate-operation"
            && row.Value.Contains("1", StringComparison.Ordinal));
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        result.Value.Should().NotContain(row =>
            row.Key == "status" && row.Value == "No conflicts detected");
    }

    [Fact]
    public async Task DetectConflictsAsync_MultipleUnevaluableOperations_ReportsOneBoundedCount()
    {
        var proposalId = Guid.NewGuid();
        var proposal = CreateProposal(
            RiskLevel.Low,
            CreateMoveOperation(proposalId, 0, "not-json{"),
            CreateMoveOperation(proposalId, 1, "[]"));

        var result = await _detector.DetectConflictsAsync(proposal, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Warn
            && row.Key == "unable-to-evaluate-operation"
            && row.Value.Contains("2", StringComparison.Ordinal));
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
    }

    [Fact]
    public async Task DetectConflictsAsync_IncompleteReview_DoesNotEmitPositiveSignalsFromOtherChecks()
    {
        var proposal = CreateProposal(
            RiskLevel.High,
            CreateMoveOperation(Guid.NewGuid(), 0, "not-json{"));

        var result = await _detector.DetectConflictsAsync(proposal, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(row => row.Key == "high-risk");
        result.Value.Should().Contain(row => row.Key == "unable-to-evaluate-operation");
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
    }

    [Fact]
    public async Task DetectConflictsAsync_IncompleteReview_RecordsStructuredCountWithoutPayloadContent()
    {
        var proposalId = Guid.NewGuid();
        var proposal = CreateProposal(
            RiskLevel.Low,
            CreateMoveOperation(proposalId, 0, "not-json{"),
            CreateMoveOperation(proposalId, 1, "[]"));

        await _detector.DetectConflictsAsync(proposal, _userId);

        var entry = _logger.Entries.Should().ContainSingle(log => log.Level == LogLevel.Warning).Subject;
        entry.Properties.Should().ContainKey("UnevaluatedOperationCount")
            .WhoseValue.Should().Be(2);
        entry.Message.Should().Contain("unevaluated_operation_count=2");
        entry.Message.Should().NotContain("not-json");
        entry.Message.Should().NotContain("[]");
    }

    [Fact]
    public async Task DetectConflictsAsync_UnsupportedIdentifierAction_FailsClosedWithoutPayloadLeak()
    {
        const string privatePayload = "unsupported-action-private-payload";
        var proposalId = Guid.NewGuid();
        var proposal = CreateProposal(
            RiskLevel.Low,
            new ProposalOperationDto(
                Id: Guid.NewGuid(),
                ProposalId: proposalId,
                Sequence: 0,
                ActionType: "future-card-action",
                TargetType: "card",
                TargetId: _cardId.ToString("D"),
                Parameters: JsonSerializer.Serialize(new { cardId = _cardId, privatePayload }),
                IdempotencyKey: $"unsupported-{Guid.NewGuid():N}",
                ExpectedVersion: null));

        var result = await _detector.DetectConflictsAsync(proposal, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Warn
            && row.Key == "unable-to-evaluate-operation"
            && row.Value.Contains("1", StringComparison.Ordinal));
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        result.Value.Should().NotContain(row =>
            row.Key == "status" && row.Value == "No conflicts detected");
        result.Value.Should().NotContain(row => row.Value.Contains(privatePayload, StringComparison.Ordinal));

        var entry = _logger.Entries.Should().ContainSingle(log => log.Level == LogLevel.Warning).Subject;
        entry.Properties.Should().ContainKey("UnevaluatedOperationCount")
            .WhoseValue.Should().Be(1);
        entry.Message.Should().NotContain(privatePayload);
    }

    [Fact]
    public async Task DetectConflictsAsync_ValidMove_RetainsExistingNoConflictResult()
    {
        var proposal = CreateProposal(
            RiskLevel.Low,
            CreateMoveOperation(
                Guid.NewGuid(),
                0,
                JsonSerializer.Serialize(new { cardId = _cardId, columnId = _targetColumnId })));

        var result = await _detector.DetectConflictsAsync(proposal, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Ok
            && row.Key == "status"
            && row.Value == "No conflicts detected");
        result.Value.Should().NotContain(row => row.Key == "unable-to-evaluate-operation");
        _logger.Entries.Should().BeEmpty();
    }

    private ProposalDto CreateProposal(RiskLevel riskLevel, params ProposalOperationDto[] operations)
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(1);
        return new ProposalDto(
            Id: operations.FirstOrDefault()?.ProposalId ?? Guid.NewGuid(),
            SourceType: ProposalSourceType.Chat,
            SourceReferenceId: null,
            BoardId: _boardId,
            RequestedByUserId: _userId,
            Status: ProposalStatus.PendingReview,
            RiskLevel: riskLevel,
            Summary: "Review operation evaluability",
            DiffPreview: null,
            ValidationIssues: null,
            CreatedAt: createdAt,
            UpdatedAt: createdAt,
            ExpiresAt: createdAt.AddHours(1).UtcDateTime,
            DecidedAt: null,
            DecidedByUserId: null,
            AppliedAt: null,
            FailureReason: null,
            CorrelationId: "test-conflict-evaluability",
            Operations: operations.ToList());
    }

    private ProposalOperationDto CreateMoveOperation(Guid proposalId, int sequence, string parameters)
    {
        return new ProposalOperationDto(
            Id: Guid.NewGuid(),
            ProposalId: proposalId,
            Sequence: sequence,
            ActionType: "move",
            TargetType: "card",
            TargetId: _cardId.ToString("D"),
            Parameters: parameters,
            IdempotencyKey: $"move-{sequence}-{Guid.NewGuid():N}",
            ExpectedVersion: null);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> structuredState
                ? structuredState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);
}
