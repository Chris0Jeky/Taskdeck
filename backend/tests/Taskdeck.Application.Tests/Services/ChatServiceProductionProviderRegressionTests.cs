using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ChatServiceProductionProviderRegressionTests
{
    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    [InlineData("Ollama")]
    public async Task SendMessageAsync_CustomClarificationPrompt_ShouldKeepAutomaticProposalCreation(
        string providerName)
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var session = new ChatSession(userId, "Provider regression", boardId);
        var unitOfWork = new Mock<IUnitOfWork>();
        var sessions = new Mock<IChatSessionRepository>();
        var messages = new Mock<IChatMessageRepository>();
        var users = new Mock<IUserRepository>();
        var planner = new Mock<IAutomationPlannerService>();
        var proposalService = new Mock<IAutomationProposalService>();
        var policyEngine = new Mock<IAutomationPolicyEngine>();

        unitOfWork.SetupGet(work => work.ChatSessions).Returns(sessions.Object);
        unitOfWork.SetupGet(work => work.ChatMessages).Returns(messages.Object);
        unitOfWork.SetupGet(work => work.Users).Returns(users.Object);
        unitOfWork
            .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        sessions
            .Setup(repository => repository.GetByIdWithMessagesAsync(
                session.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        messages
            .Setup(repository => repository.AddAsync(
                It.IsAny<ChatMessage>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage message, CancellationToken _) => message);
        planner
            .Setup(service => service.ParseInstructionAsync(
                It.IsAny<string>(),
                userId,
                boardId,
                It.IsAny<CancellationToken>(),
                ProposalSourceType.Chat,
                session.Id.ToString(),
                It.IsAny<string?>()))
            .ReturnsAsync(Result.Success(BuildProposal(proposalId, userId, boardId)));

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    BuildProviderResponse(providerName),
                    Encoding.UTF8,
                    "application/json")
            });
        var provider = BuildProvider(providerName, new HttpClient(handler));
        var service = new ChatService(
            unitOfWork.Object,
            provider,
            planner.Object,
            proposalService.Object,
            policyEngine.Object);

        var result = await service.SendMessageAsync(
            session.Id,
            userId,
            new SendChatMessageDto("create card 'Fix login bug'"));

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().Be("proposal-reference");
        result.Value.ProposalId.Should().Be(proposalId);
        planner.Verify(service => service.ParseInstructionAsync(
            It.IsAny<string>(),
            userId,
            boardId,
            It.IsAny<CancellationToken>(),
            ProposalSourceType.Chat,
            session.Id.ToString(),
            It.IsAny<string?>()), Times.Once);
    }

    private static ILlmProvider BuildProvider(string providerName, HttpClient httpClient)
    {
        var settings = new LlmProviderSettings
        {
            EnableLiveProviders = true,
            AllowLiveProvidersInDevelopment = true,
            Provider = providerName,
            OpenAi = new OpenAiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                TimeoutSeconds = 30
            },
            Gemini = new GeminiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                Model = "gemini-2.5-flash",
                TimeoutSeconds = 30
            },
            Ollama = new OllamaProviderSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "llama3.2",
                TimeoutSeconds = 30,
                AllowLocalhostEndpoints = true
            }
        };

        return providerName switch
        {
            "OpenAI" => new OpenAiLlmProvider(
                httpClient,
                settings,
                NullLogger<OpenAiLlmProvider>.Instance),
            "Gemini" => new GeminiLlmProvider(
                httpClient,
                settings,
                NullLogger<GeminiLlmProvider>.Instance),
            "Ollama" => new OllamaLlmProvider(
                httpClient,
                settings,
                NullLogger<OllamaLlmProvider>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };
    }

    private static string BuildProviderResponse(string providerName)
    {
        return providerName switch
        {
            "OpenAI" => JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new { content = "I'll create that card." },
                        finish_reason = "stop"
                    }
                },
                usage = new { total_tokens = 7 }
            }),
            "Gemini" => JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new { parts = new[] { new { text = "I'll create that card." } } },
                        finishReason = "STOP"
                    }
                },
                usageMetadata = new { totalTokenCount = 7 }
            }),
            "Ollama" => JsonSerializer.Serialize(new
            {
                message = new { content = "I'll create that card." },
                done = true,
                eval_count = 7,
                done_reason = "stop"
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };
    }

    private static ProposalDto BuildProposal(Guid proposalId, Guid userId, Guid boardId)
    {
        return new ProposalDto(
            proposalId,
            ProposalSourceType.Chat,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "Create card",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddHours(1),
            null,
            null,
            null,
            null,
            "correlation",
            []);
    }
}
