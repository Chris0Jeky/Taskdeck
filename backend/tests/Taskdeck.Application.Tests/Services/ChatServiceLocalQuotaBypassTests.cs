using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class ChatServiceLocalQuotaBypassTests
{
    [Fact]
    public async Task SendMessageAsync_ChecklistBootstrapWithoutBoard_DoesNotReserveLlmQuota()
    {
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var sessions = new Mock<IChatSessionRepository>(MockBehavior.Strict);
        var messages = new Mock<IChatMessageRepository>(MockBehavior.Strict);
        var provider = new Mock<ILlmProvider>(MockBehavior.Strict);
        var quota = new Mock<ILlmQuotaService>(MockBehavior.Strict);
        var planner = new Mock<IAutomationPlannerService>(MockBehavior.Strict);
        var proposals = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        var policy = new Mock<IAutomationPolicyEngine>(MockBehavior.Strict);
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Local checklist bootstrap");
        using var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;

        unitOfWork.SetupGet(candidate => candidate.ChatSessions).Returns(sessions.Object);
        unitOfWork.SetupGet(candidate => candidate.ChatMessages).Returns(messages.Object);
        unitOfWork
            .Setup(candidate => candidate.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);
        sessions
            .Setup(candidate => candidate.GetByIdWithMessagesAsync(session.Id, cancellationToken))
            .ReturnsAsync(session);
        messages
            .Setup(candidate => candidate.AddAsync(It.IsAny<ChatMessage>(), cancellationToken))
            .ReturnsAsync((ChatMessage message, CancellationToken _) => message);

        var service = new ChatService(
            unitOfWork.Object,
            provider.Object,
            planner.Object,
            proposals.Object,
            policy.Object,
            quotaService: quota.Object);

        var result = await service.SendMessageAsync(
            session.Id,
            userId,
            new SendChatMessageDto(
                """
                Project checklist:
                - [ ] Set up the backlog
                - [ ] Plan the release
                """,
                RequestProposal: true),
            cancellationToken);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.MessageType.Should().Be("action-needs-board");
        result.Value.Content.Should().Contain("writable board");
        provider.VerifyNoOtherCalls();
        quota.VerifyNoOtherCalls();
    }
}
