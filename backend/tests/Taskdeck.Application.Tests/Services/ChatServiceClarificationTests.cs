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

        // Use the real MockLlmProvider to exercise clarification behavior
        _service = new ChatService(
            _unitOfWorkMock.Object,
            new MockLlmProvider(),
            _plannerMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _notificationServiceMock.Object);
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
    public async Task SendMessage_ShouldForceBestEffort_AfterMaxClarificationRounds()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Test session");

        // Simulate 2 clarification rounds already completed
        session.AddMessage(new ChatMessage(session.Id, ChatMessageRole.User, "Create tasks for onboarding"));
        session.AddMessage(new ChatMessage(session.Id, ChatMessageRole.Assistant, "How many tasks?", "clarification"));
        session.AddMessage(new ChatMessage(session.Id, ChatMessageRole.User, "3 tasks"));
        session.AddMessage(new ChatMessage(session.Id, ChatMessageRole.Assistant, "Which column?", "clarification"));

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        // After max rounds, even an ambiguous request should not get clarification
        var result = await _service.SendMessageAsync(
            session.Id, userId,
            new SendChatMessageDto("put them in Backlog"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().NotBe("clarification");
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
