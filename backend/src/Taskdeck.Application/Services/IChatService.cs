using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IChatService
{
    Task<Result<ChatSessionDto>> CreateSessionAsync(Guid userId, CreateChatSessionDto dto, CancellationToken ct = default);
    Task<Result<ChatSessionDto>> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<Result<IEnumerable<ChatSessionDto>>> GetUserSessionsAsync(Guid userId, CancellationToken ct = default);
    Task<Result<ChatMessageDto>> SendMessageAsync(Guid sessionId, Guid userId, SendChatMessageDto dto, CancellationToken ct = default);
    IAsyncEnumerable<LlmTokenEvent> StreamResponseAsync(Guid sessionId, CancellationToken ct = default);
}
