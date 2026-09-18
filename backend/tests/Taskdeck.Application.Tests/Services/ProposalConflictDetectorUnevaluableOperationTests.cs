using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class ProposalConflictDetectorUnevaluableOperationTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAutomationProposalRepository> _proposals = new();
    private readonly Mock<ICardRepository> _cards = new();
    private readonly Mock<IColumnRepository> _columns = new();
    private readonly Mock<ICardCommentRepository> _comments = new();
    private readonly Mock<IOutboundWebhookSubscriptionRepository> _webhooks = new();
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly RecordingLogger<ProposalConflictEvaluationGuard> _logger = new();

    public ProposalConflictDetectorUnevaluableOperationTests()
    {
        _unitOfWork.Setup(unit => unit.AutomationProposals).Returns(_proposals.Object);
        _unitOfWork.Setup(unit => unit.Cards).Returns(_cards.Object);
        _unitOfWork.Setup(unit => unit.Columns).Returns(_columns.Object);
        _unitOfWork.Setup(unit => unit.CardComments).Returns(_comments.Object);
        _unitOfWork.Setup(unit => unit.OutboundWebhookSubscriptions).Returns(_webhooks.Object);

        _authorization
            .Setup(service => service.CanReadBoardAsync(_userId, _boardId))
            .ReturnsAsync(Result.Success(true));
        _comments
            .Setup(repository => repository.CountByCardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _proposals
            .Setup(repository => repository.GetPendingByOperationTargetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _webhooks
            .Setup(repository => repository.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Theory]
    [InlineData("move", "not-json{", true)]
    [InlineData("create", "not-json{", false)]
    [InlineData("move", "[]", true)]
    [InlineData("move", "{\"columnId\":12345}", true)]
    [InlineData("move", "{\"columnId\":\"not-a-guid\"}", true)]
    [InlineData("move", "{\"destination\":\"unsupported\"}", true)]
    public async Task DetectConflictsAsync_UnevaluableDestinationShape_ReturnsBoundedWarningWithoutPositiveEvidence(
        string actionType,
        string parameters,
        bool targetsExistingCard)
    {
        var cardId = Guid.NewGuid();
        var card = new Card(cardId, _boardId, Guid.NewGuid(), "Source card");
        var proposal = CreateProposal();
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType,
            targetType: "card",
            parameters,
            Guid.NewGuid().ToString(),
            targetsExistingCard ? cardId.ToString() : null));

        ArrangePersistedProposal(proposal);
        _cards
            .Setup(repository => repository.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var result = await CreateDetector().DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        var warning = result.Value.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Warn && row.Key == "unable-to-evaluate-operation").Subject;
        warning.Value.Should().Be("1 proposal operation could not be evaluated");
        warning.Value.Should().NotContain(parameters);
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        result.Value.Should().NotContain(row => row.Key == "status");
        _logger.Messages.Should().ContainSingle(message =>
            message.Contains("unevaluated_operation_count=1", StringComparison.Ordinal));
        _logger.Messages.Single().Should().NotContain(parameters);
    }

    [Fact]
    public async Task DetectConflictsAsync_MultipleUnevaluableOperations_ReportsCountOnce()
    {
        var cardId = Guid.NewGuid();
        var card = new Card(cardId, _boardId, Guid.NewGuid(), "Source card");
        var proposal = CreateProposal();
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", "[]", Guid.NewGuid().ToString()));
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 1, "move", "card", "{\"columnId\":false}",
            Guid.NewGuid().ToString(), cardId.ToString()));

        ArrangePersistedProposal(proposal);
        _cards
            .Setup(repository => repository.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var result = await CreateDetector().DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        var warning = result.Value.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Warn && row.Key == "unable-to-evaluate-operation").Subject;
        warning.Value.Should().Be("2 proposal operations could not be evaluated");
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        _logger.Messages.Should().ContainSingle(message =>
            message.Contains("unevaluated_operation_count=2", StringComparison.Ordinal));
        _logger.Messages.Single().Should().NotContain("columnId");
    }

    [Fact]
    public async Task DetectConflictsAsync_ValidMove_RetainsExistingConflictAndCapacitySignals()
    {
        var cardId = Guid.NewGuid();
        var targetColumnId = Guid.NewGuid();
        var card = new Card(cardId, _boardId, Guid.NewGuid(), "Source card");
        var targetColumn = new Column(_boardId, "Ready", position: 1, wipLimit: 3);
        var proposal = CreateProposal(RiskLevel.High);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType: "move",
            targetType: "card",
            parameters: $"{{\"columnId\":\"{targetColumnId}\"}}",
            idempotencyKey: Guid.NewGuid().ToString(),
            targetId: cardId.ToString()));

        ArrangePersistedProposal(proposal);
        _cards
            .Setup(repository => repository.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        _columns
            .Setup(repository => repository.GetByIdWithCardsAsync(targetColumnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetColumn);

        var result = await CreateDetector().DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(row => row.Key == "high-risk" && row.Tone == ConflictTone.Warn);
        result.Value.Should().Contain(row => row.Key == "capacity" && row.Tone == ConflictTone.Ok);
        result.Value.Should().NotContain(row => row.Key == "unable-to-evaluate-operation");
        _logger.Messages.Should().BeEmpty();
    }

    private IProposalConflictDetector CreateDetector()
    {
        var inner = new ProposalConflictDetector(
            _unitOfWork.Object,
            _authorization.Object);
        return new ProposalConflictEvaluationGuard(
            inner,
            _unitOfWork.Object,
            _logger);
    }

    private AutomationProposal CreateProposal(RiskLevel riskLevel = RiskLevel.Low) =>
        new(
            ProposalSourceType.Chat,
            _userId,
            "Review destination safety",
            riskLevel,
            Guid.NewGuid().ToString(),
            _boardId);

    private void ArrangePersistedProposal(AutomationProposal proposal)
    {
        _proposals
            .Setup(repository => repository.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
