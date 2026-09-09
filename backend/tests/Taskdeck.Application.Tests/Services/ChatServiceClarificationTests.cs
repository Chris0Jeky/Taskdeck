using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ChatServiceClarificationTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IChatSessionRepository> _chatSessionRepoMock = new();
    private readonly Mock<IChatMessageRepository> _chatMessageRepoMock = new();
    private readonly Mock<IColumnRepository> _columnRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IAutomationPlannerService> _plannerMock = new();
    private readonly Mock<IAutomationProposalService> _proposalServiceMock = new();
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();
    private readonly ChatService _service;

    public ChatServiceClarificationTests()
    {
        _unitOfWorkMock.SetupGet(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _chatMessageRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ChatMessage>(), default))
            .ReturnsAsync((ChatMessage message, CancellationToken _) => message);
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));
        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success(true));

        // Use the real MockLlmProvider to exercise clarification behavior
        _service = new ChatService(
            _unitOfWorkMock.Object,
            new MockLlmProvider(),
            _plannerMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _notificationServiceMock.Object,
            _authorizationServiceMock.Object);
    }

    [Fact]
    public async Task SendMessage_ShouldReturnClarification_ForAmbiguousRequest()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Test session");

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        var result = await _service.SendMessageAsync(
            session.Id, userId,
            new SendChatMessageDto("create onboarding tasks for new hires"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().Be("clarification");
        result.Value.Content.Should().Contain("Could you tell me");
    }

    [Fact]
    public async Task SendMessage_ShouldTellUserNoBoardIsLinked_OnClarificationTurnWithoutBoardScope()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Test session");

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        var result = await _service.SendMessageAsync(
            session.Id, userId,
            new SendChatMessageDto("create onboarding tasks for new hires"),
            default);

        result.IsSuccess.Should().BeTrue();
        // The type stays "clarification" so the round still counts toward best-effort forcing
        // and the composer still offers the skip action (#2004).
        result.Value.MessageType.Should().Be("clarification");
        result.Value.Content.Should().Contain("Could you tell me");
        result.Value.Content.Should().Contain("nothing was created or changed on any board");
        result.Value.Content.Should().Contain("Select a writable board below");
    }

    [Fact]
    public async Task SendMessage_ShouldNotAddNoBoardNotice_OnClarificationTurnWithBoardScope()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Test session", Guid.NewGuid());

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        var result = await _service.SendMessageAsync(
            session.Id, userId,
            new SendChatMessageDto("create onboarding tasks for new hires"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().Be("clarification");
        result.Value.Content.Should().Contain("Could you tell me");
        result.Value.Content.Should().NotContain("nothing was created or changed on any board");
    }

    [Fact]
    public async Task SendMessage_ShouldNotClarify_ForClearActionableRequest()
    {
        var userId = Guid.NewGuid();
        // No board ID — avoids proposal creation path, focuses on clarification check
        var session = new ChatSession(userId, "Test session");

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        // "create card" is a clear actionable pattern that should not trigger clarification
        var result = await _service.SendMessageAsync(
            session.Id, userId,
            new SendChatMessageDto("create card 'Fix login bug'"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().NotBe("clarification");
    }

    [Fact]
    public async Task SendMessage_ShouldSkipClarification_WhenUserSaysJustDoYourBest()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Test session");

        // Simulate a session that already has a clarification round
        var assistantClarification = new ChatMessage(
            session.Id, ChatMessageRole.Assistant,
            "Could you tell me more details?", "clarification");
        session.AddMessage(assistantClarification);

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        var result = await _service.SendMessageAsync(
            session.Id, userId,
            new SendChatMessageDto("just do your best"),
            default);

        result.IsSuccess.Should().BeTrue();
        // Should NOT return clarification when user explicitly skips
        result.Value.MessageType.Should().NotBe("clarification");
    }

    [Fact]
    public async Task SendMessage_ShouldForceBestEffort_AfterOnePersistedClarificationRound()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Test session");

        // Simulate the persisted original intent and one assistant clarification.
        session.AddMessage(new ChatMessage(session.Id, ChatMessageRole.User, "Create tasks for onboarding"));
        session.AddMessage(new ChatMessage(session.Id, ChatMessageRole.Assistant, "How many tasks?", "clarification"));

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        // The plain answer completes the single allowed round after reload.
        var result = await _service.SendMessageAsync(
            session.Id, userId,
            new SendChatMessageDto("3 tasks in Backlog"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().NotBe("clarification");
    }

    [Fact]
    public async Task SendMessage_ShouldAttemptOriginalIntentWithPlainClarificationAnswer()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var session = new ChatSession(userId, "Reloaded clarification", boardId);
        session.AddMessage(new ChatMessage(
            session.Id,
            ChatMessageRole.User,
            "create card for the release follow-up"));
        session.AddMessage(new ChatMessage(
            session.Id,
            ChatMessageRole.Assistant,
            "What should the card be called?",
            "clarification"));
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);
        _plannerMock
            .Setup(planner => planner.ParseInstructionAsync(
                It.Is<string>(instruction =>
                    instruction.Contains("create card for the release follow-up") &&
                    instruction.Contains("Clarification answer: Ship notes")),
                userId,
                boardId,
                It.IsAny<CancellationToken>(),
                ProposalSourceType.Chat,
                session.Id.ToString(),
                It.IsAny<string?>()))
            .ReturnsAsync(Result.Success(new ProposalDto(
                proposalId,
                ProposalSourceType.Chat,
                null,
                boardId,
                userId,
                ProposalStatus.PendingReview,
                RiskLevel.Low,
                "Create release follow-up",
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTime.UtcNow.AddHours(1),
                null,
                null,
                null,
                null,
                "corr",
                new List<ProposalOperationDto>())));

        var result = await _service.SendMessageAsync(
            session.Id,
            userId,
            new SendChatMessageDto("Ship notes"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().Be("proposal-reference");
        result.Value.ProposalId.Should().Be(proposalId);
    }

    [Fact]
    public async Task MockProvider_ShouldReturnClarification_ForAmbiguousInput()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create onboarding tasks for non-technical people")
            });

        var result = await provider.CompleteAsync(request);

        result.IsClarificationRequest.Should().BeTrue();
        result.IsActionable.Should().BeFalse();
        result.Content.Should().Contain("Could you tell me");
    }

    [Fact]
    public async Task MockProvider_ShouldNotClarify_WhenForcingBestEffort()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create onboarding tasks for non-technical people")
            },
            SystemPrompt: "Do NOT ask any more questions. Generate your best-effort instructions.");

        var result = await provider.CompleteAsync(request);

        result.IsClarificationRequest.Should().BeFalse();
    }

    [Fact]
    public async Task MockProvider_ShouldNotClarify_ForClearActionableRequest()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card 'Fix login bug'")
            });

        var result = await provider.CompleteAsync(request);

        result.IsClarificationRequest.Should().BeFalse();
        result.IsActionable.Should().BeTrue();
    }
}
