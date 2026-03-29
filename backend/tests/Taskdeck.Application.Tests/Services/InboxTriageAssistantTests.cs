using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class InboxTriageAssistantTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAgentPolicyEvaluator> _policyMock;
    private readonly Mock<IAutomationProposalService> _proposalServiceMock;
    private readonly Mock<ILlmQueueRepository> _llmQueueRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly InMemoryLogger<InboxTriageAssistant> _logger;
    private readonly InboxTriageAssistant _assistant;

    private readonly Guid _agentProfileId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    public InboxTriageAssistantTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _policyMock = new Mock<IAgentPolicyEvaluator>();
        _proposalServiceMock = new Mock<IAutomationProposalService>();
        _llmQueueRepoMock = new Mock<ILlmQueueRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _logger = new InMemoryLogger<InboxTriageAssistant>();

        _unitOfWorkMock.Setup(u => u.LlmQueue).Returns(_llmQueueRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);

        _assistant = new InboxTriageAssistant(
            _unitOfWorkMock.Object,
            _policyMock.Object,
            _proposalServiceMock.Object,
            _logger);
    }

    private void SetupPolicyAllow()
    {
        _policyMock.Setup(p => p.EvaluateToolUseAsync(
                _agentProfileId, InboxTriageAssistant.ToolKey, It.IsAny<IDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDecision.AllowWithReview("Medium-risk tool requires review."));
    }

    private void SetupPolicyDeny(string reason = "Denied by policy.")
    {
        _policyMock.Setup(p => p.EvaluateToolUseAsync(
                _agentProfileId, InboxTriageAssistant.ToolKey, It.IsAny<IDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDecision.Deny(reason));
    }

    private void SetupInboxItems(int count)
    {
        var items = Enumerable.Range(0, count)
            .Select(_ => new LlmRequest(_userId, "capture", "Task item text"))
            .ToList();

        _llmQueueRepoMock.Setup(r => r.GetByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
    }

    private void SetupBoardWithColumn()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        _boardRepoMock.Setup(r => r.GetByIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var column = TestDataBuilder.CreateColumn(_boardId, "To Do", 0);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { column });
    }

    private void SetupProposalCreationSuccess()
    {
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateProposalDto dto, CancellationToken _) =>
            {
                var proposal = new ProposalDto(
                    Guid.NewGuid(),
                    dto.SourceType,
                    dto.SourceReferenceId,
                    dto.BoardId,
                    dto.RequestedByUserId,
                    ProposalStatus.PendingReview,
                    dto.RiskLevel,
                    dto.Summary,
                    null, null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    DateTime.UtcNow.AddDays(1),
                    null, null, null, null,
                    dto.CorrelationId,
                    new List<ProposalOperationDto>());
                return Result.Success(proposal);
            });
    }

    [Fact]
    public async Task RunTriage_ShouldFail_WhenAgentProfileIdIsEmpty()
    {
        var result = await _assistant.RunTriageAsync(Guid.Empty, _userId, _boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task RunTriage_ShouldFail_WhenUserIdIsEmpty()
    {
        var result = await _assistant.RunTriageAsync(_agentProfileId, Guid.Empty, _boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task RunTriage_ShouldFail_WhenBoardIdIsEmpty()
    {
        var result = await _assistant.RunTriageAsync(_agentProfileId, _userId, Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task RunTriage_ShouldFail_WhenPolicyDenies()
    {
        SetupPolicyDeny("Tool not in allowlist.");

        var result = await _assistant.RunTriageAsync(_agentProfileId, _userId, _boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("not in allowlist");
    }

    [Fact]
    public async Task RunTriage_ShouldFail_WhenNoInboxItems()
    {
        SetupPolicyAllow();
        SetupInboxItems(0);

        var result = await _assistant.RunTriageAsync(_agentProfileId, _userId, _boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task RunTriage_ShouldFail_WhenBoardNotFound()
    {
        SetupPolicyAllow();
        SetupInboxItems(2);
        _boardRepoMock.Setup(r => r.GetByIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board?)null);

        var result = await _assistant.RunTriageAsync(_agentProfileId, _userId, _boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task RunTriage_ShouldFail_WhenBoardHasNoColumns()
    {
        SetupPolicyAllow();
        SetupInboxItems(2);

        var board = TestDataBuilder.CreateBoard("Test Board");
        _boardRepoMock.Setup(r => r.GetByIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Column>());

        var result = await _assistant.RunTriageAsync(_agentProfileId, _userId, _boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("no columns");
    }

    [Fact]
    public async Task RunTriage_ShouldCreateProposal_WithCorrectItemCount()
    {
        SetupPolicyAllow();
        SetupInboxItems(3);
        SetupBoardWithColumn();
        SetupProposalCreationSuccess();

        var result = await _assistant.RunTriageAsync(_agentProfileId, _userId, _boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ItemsTriaged.Should().Be(3);
        result.Value.RequiresReview.Should().BeTrue();
        result.Value.ProposalId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunTriage_ShouldNeverDirectlyMutateBoard()
    {
        SetupPolicyAllow();
        SetupInboxItems(2);
        SetupBoardWithColumn();
        SetupProposalCreationSuccess();

        await _assistant.RunTriageAsync(_agentProfileId, _userId, _boardId);

        // Verify that the assistant never called SaveChangesAsync (no direct mutation)
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        // Verify that proposal was created (the review-first path)
        _proposalServiceMock.Verify(
            s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunTriage_ShouldRouteProposalThroughPolicyEvaluator()
    {
        SetupPolicyAllow();
        SetupInboxItems(1);
        SetupBoardWithColumn();
        SetupProposalCreationSuccess();

        await _assistant.RunTriageAsync(_agentProfileId, _userId, _boardId);

        _policyMock.Verify(
            p => p.EvaluateToolUseAsync(
                _agentProfileId,
                InboxTriageAssistant.ToolKey,
                It.IsAny<IDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunTriage_ShouldPassBoardIdInPolicyContext()
    {
        SetupPolicyAllow();
        SetupInboxItems(1);
        SetupBoardWithColumn();
        SetupProposalCreationSuccess();

        await _assistant.RunTriageAsync(_agentProfileId, _userId, _boardId);

        _policyMock.Verify(
            p => p.EvaluateToolUseAsync(
                _agentProfileId,
                InboxTriageAssistant.ToolKey,
                It.Is<IDictionary<string, string>>(ctx => ctx.ContainsKey("boardId")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void GetToolDefinition_ShouldReturnCorrectDefinition()
    {
        var tool = InboxTriageAssistant.GetToolDefinition();

        tool.Key.Should().Be("inbox.triage");
        tool.DisplayName.Should().Be("Inbox Triage");
        tool.Scope.Should().Be(ToolScope.Inbox);
        tool.RiskLevel.Should().Be(ToolRiskLevel.Medium);
    }
}
