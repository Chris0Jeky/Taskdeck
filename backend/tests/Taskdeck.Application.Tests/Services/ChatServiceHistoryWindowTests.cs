using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ChatServiceHistoryWindowTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IChatSessionRepository> _chatSessionRepoMock = new();
    private readonly Mock<IChatMessageRepository> _chatMessageRepoMock = new();
    private readonly Mock<ILlmProvider> _llmProviderMock = new();
    private readonly Mock<IAutomationPlannerService> _plannerMock = new();
    private readonly Mock<IAutomationProposalService> _proposalServiceMock = new();
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();

    public ChatServiceHistoryWindowTests()
    {
        _unitOfWorkMock.SetupGet(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _chatMessageRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ChatMessage>(), default))
            .ReturnsAsync((ChatMessage message, CancellationToken _) => message);
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .ReturnsAsync(new LlmCompletionResult("Assistant response", 12, false, null));
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));
        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success(true));
    }

    private ChatService CreateService(LlmToolCallingSettings settings)
        => new(
            _unitOfWorkMock.Object,
            _llmProviderMock.Object,
            _plannerMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _notificationServiceMock.Object,
            _authorizationServiceMock.Object,
            toolCallingSettings: settings);

    [Fact]
    public async Task SendMessageAsync_ShouldSendOnlyRecentHistory_WhenSessionExceedsMaxHistoryMessages()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Long session");
        SeedHistory(session, 10);
        var settings = new LlmToolCallingSettings { MaxHistoryMessages = 4 };

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .Callback<ChatCompletionRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new LlmCompletionResult("Assistant response", 12, false, null));

        var result = await CreateService(settings).SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("latest user question"), default);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.Should().HaveCount(4);
        capturedRequest.Messages.Select(m => m.Content).Should().Equal(
            "history message 8", "history message 9", "history message 10", "latest user question");
    }

    [Fact]
    public async Task SendMessageAsync_ShouldSendFullHistory_WhenSessionIsWithinMaxHistoryMessages()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Short session");
        SeedHistory(session, 2);
        var settings = new LlmToolCallingSettings { MaxHistoryMessages = 50 };

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .Callback<ChatCompletionRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new LlmCompletionResult("Assistant response", 12, false, null));

        var result = await CreateService(settings).SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("hello"), default);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldSendLatestMessageOnly_WhenMaxHistoryMessagesIsMisconfigured()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Misconfigured session");
        SeedHistory(session, 5);
        var settings = new LlmToolCallingSettings { MaxHistoryMessages = 0 };

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .Callback<ChatCompletionRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new LlmCompletionResult("Assistant response", 12, false, null));

        var result = await CreateService(settings).SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("latest user question"), default);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.Should().ContainSingle()
            .Which.Content.Should().Be("latest user question");
    }

    [Fact]
    public async Task StreamResponseAsync_ShouldSendOnlyRecentHistory_WhenSessionExceedsMaxHistoryMessages()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Long streaming session");
        SeedHistory(session, 10);
        var lastUserMessage = new ChatMessage(session.Id, ChatMessageRole.User, "stream my answer");
        session.AddMessage(lastUserMessage);
        var settings = new LlmToolCallingSettings { MaxHistoryMessages = 4 };

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(p => p.StreamAsync(It.IsAny<ChatCompletionRequest>(), default))
            .Returns((ChatCompletionRequest request, CancellationToken _) =>
            {
                capturedRequest = request;
                return StreamEvents();
            });

        await foreach (var _ in CreateService(settings).StreamResponseAsync(session.Id, userId, default)) { }

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.Should().HaveCount(4);
        capturedRequest.Messages.Select(m => m.Content).Should().Equal(
            "history message 8", "history message 9", "history message 10", "stream my answer");
    }

    private static void SeedHistory(ChatSession session, int count)
    {
        // Seeded messages need distinct, increasing timestamps: Messages orders by
        // CreatedAt with a random-Guid tie-break, so identical timestamps would be flaky.
        var baseTime = DateTimeOffset.UtcNow.AddHours(-2);
        for (var i = 1; i <= count; i++)
        {
            var role = i % 2 == 1 ? ChatMessageRole.User : ChatMessageRole.Assistant;
            var message = new ChatMessage(session.Id, role, $"history message {i}");
            SetCreatedAt(message, baseTime.AddMinutes(i));
            session.AddMessage(message);
        }
    }

    private static void SetCreatedAt(Entity entity, DateTimeOffset timestamp)
        => typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(entity, timestamp);

    private static async IAsyncEnumerable<LlmTokenEvent> StreamEvents()
    {
        yield return new LlmTokenEvent("token", true, TokensUsed: 10, Provider: "Mock", Model: "mock-default");
        await Task.CompletedTask;
    }
}
