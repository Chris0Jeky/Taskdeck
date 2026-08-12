using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Consumer-level regression for issue #1617: the OpenAI-compatible buffered path sanitizes
/// content_filter and refusal responses to empty content, and ChatMessage rejects empty content.
/// These tests drive the real <see cref="OpenAiCompatibleLlmProvider"/> through
/// <see cref="ChatService"/> and prove a valid assistant-history record is persisted, that no
/// upstream refusal text escapes, and that quota settlement, provenance, and circuit-success
/// semantics are unchanged.
/// </summary>
public class ChatServiceOpenAiCompatibleRefusalTests
{
    private const string SanitizedPlaceholder = "The provider ended the response without returning text.";

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
    private readonly List<ChatMessage> _persistedMessages = new();
    private int _saveChangesCalls;

    public ChatServiceOpenAiCompatibleRefusalTests()
    {
        _unitOfWorkMock.SetupGet(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(default))
            .ReturnsAsync(() => ++_saveChangesCalls);
        _chatMessageRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ChatMessage>(), default))
            .ReturnsAsync((ChatMessage message, CancellationToken _) =>
            {
                _persistedMessages.Add(message);
                return message;
            });
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));
    }

    [Theory]
    [InlineData(
        """
        {"choices":[{"message":{"content":null,"content_filter_results":{"hate":{"filtered":true,"detail":"internal moderation detail"}}},"finish_reason":"content_filter"}],"usage":{"total_tokens":4}}
        """,
        "content filter",
        "internal moderation detail")]
    [InlineData(
        """
        {"choices":[{"message":{"content":null,"refusal":"sensitive vendor refusal detail"},"finish_reason":"stop"}],"usage":{"total_tokens":4}}
        """,
        "refused",
        "sensitive vendor refusal detail")]
    public async Task SendMessageAsync_BufferedRefusalOrContentFilter_PersistsSanitizedDegradedMessage(
        string responseBody,
        string expectedReasonFragment,
        string forbiddenUpstreamText)
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Refusal session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);
        var service = BuildService(CreateProvider(_ => JsonResponse(responseBody)));

        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Summarize the release notes"), default);

        result.IsSuccess.Should().BeTrue(
            "a sanitized degraded outcome must still produce an assistant message, not a validation failure");
        result.Value.MessageType.Should().Be("degraded");
        result.Value.Content.Should().Be(SanitizedPlaceholder);
        result.Value.DegradedReason.Should().Contain(expectedReasonFragment);

        var assistantMessage = _persistedMessages.Should().ContainSingle(m => m.Role == ChatMessageRole.Assistant).Subject;
        assistantMessage.SessionId.Should().Be(session.Id);
        assistantMessage.Content.Should().Be(SanitizedPlaceholder);
        assistantMessage.MessageType.Should().Be("degraded");
        assistantMessage.DegradedReason.Should().Contain(expectedReasonFragment);
        assistantMessage.TokenUsage.Should().Be(4);
        _saveChangesCalls.Should().BeGreaterThan(0, "the assistant-history record must be committed");

        assistantMessage.Content.Should().NotContain(forbiddenUpstreamText);
        assistantMessage.DegradedReason.Should().NotContain(forbiddenUpstreamText);
        result.Value.Content.Should().NotContain(forbiddenUpstreamText);
        result.Value.DegradedReason.Should().NotContain(forbiddenUpstreamText);
    }

    [Fact]
    public async Task SendMessageAsync_BufferedRefusal_SettlesQuotaWithProviderProvenance()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Refusal quota session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        var reservationId = Guid.NewGuid();
        var quotaMock = new Mock<ILlmQuotaService>();
        quotaMock
            .Setup(q => q.ReserveAsync(userId, LlmSurface.Chat, default))
            .ReturnsAsync(new QuotaReservationDto(true, null, reservationId, 1000, 10, EstimatedTokens: 50));
        var service = BuildService(
            CreateProvider(_ => JsonResponse(
                """{"choices":[{"message":{"content":null},"finish_reason":"content_filter"}],"usage":{"total_tokens":4}}""")),
            quotaMock.Object);

        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Summarize the release notes"), default);

        result.IsSuccess.Should().BeTrue();
        quotaMock.Verify(
            q => q.CommitReservationAsync(
                reservationId,
                userId,
                LlmSurface.Chat,
                "OpenAICompatible",
                "vendor/model",
                4,
                0,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a dispatched refusal still consumed upstream tokens and must be billed with its provenance");
        quotaMock.Verify(
            q => q.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_BufferedRefusal_DoesNotOpenTheProviderCircuit()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Refusal circuit session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        var dispatches = 0;
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 60 };
        var provider = CreateProvider(
            _ =>
            {
                dispatches++;
                return JsonResponse(
                    """{"choices":[{"message":{"content":null},"finish_reason":"content_filter"}],"usage":{"total_tokens":4}}""");
            },
            tracker,
            circuitSettings);
        var service = BuildService(provider);
        var dto = new SendChatMessageDto("Summarize the release notes");

        var first = await service.SendMessageAsync(session.Id, userId, dto, default);
        var second = await service.SendMessageAsync(session.Id, userId, dto, default);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        dispatches.Should().Be(2, "a sanitized refusal is a provider success, so the circuit stays closed");
        tracker.Get("OpenAICompatible")?.State.Should().NotBe(CircuitState.Open);
    }

    [Fact]
    public async Task SendMessageAsync_BufferedSuccess_IsUnaffectedByThePlaceholder()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Normal session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);
        var service = BuildService(CreateProvider(_ => JsonResponse(
            """{"choices":[{"message":{"content":"Here is the summary."},"finish_reason":"stop"}],"usage":{"total_tokens":6}}""")));

        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Summarize the release notes"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().Be("text");
        result.Value.Content.Should().Be("Here is the summary.");
        result.Value.DegradedReason.Should().BeNull();
    }

    private ChatService BuildService(ILlmProvider provider, ILlmQuotaService? quota = null) =>
        new(
            _unitOfWorkMock.Object,
            provider,
            _plannerMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _notificationServiceMock.Object,
            _authorizationServiceMock.Object,
            quota);

    /// <summary>
    /// Builds the real provider behind <see cref="LlmDispatchTrackingHandler"/> so the dispatch phase
    /// matches production wiring (the phase drives quota settlement).
    /// </summary>
    private static OpenAiCompatibleLlmProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        CircuitBreakerStateTracker? tracker = null,
        CircuitBreakerSettings? circuitSettings = null) =>
        new(
            new HttpClient(new LlmDispatchTrackingHandler
            {
                InnerHandler = new StubHttpMessageHandler(responseFactory)
            }),
            BuildSettings(),
            NullLogger<OpenAiCompatibleLlmProvider>.Instance,
            tracker,
            circuitSettings,
            new LlmProviderRuntimePolicy(
                AllowGeneralProviderLocalhost: false,
                AllowOllamaLocalhost: false));

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static LlmProviderSettings BuildSettings() => new()
    {
        EnableLiveProviders = true,
        Provider = "OpenAICompatible",
        OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://api.example.test/v1",
            Model = "vendor/model",
            TimeoutSeconds = 30
        }
    };
}
