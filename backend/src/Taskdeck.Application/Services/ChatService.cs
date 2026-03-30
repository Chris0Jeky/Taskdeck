using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ChatService : IChatService
{
    private const int MaxPromptLength = 4000;
    private const int MaxChecklistItemCount = 30;
    private static readonly Regex MentionRegex = new(@"(?<![A-Za-z0-9_.-])@(?<username>[A-Za-z0-9_.-]{3,50})", RegexOptions.Compiled);
    private static readonly string[] PromptInjectionDenylist =
    {
        "ignore previous instructions",
        "reveal system prompt",
        "rm -rf",
        "drop table",
        "delete every board"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILlmProvider _llmProvider;
    private readonly IAutomationPlannerService _automationPlanner;
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly INotificationService _notificationService;
    private readonly IAuthorizationService? _authorizationService;
    private readonly ILlmQuotaService? _quotaService;
    private readonly ILlmKillSwitchService? _killSwitchService;
    private readonly IBoardContextBuilder? _boardContextBuilder;

    public ChatService(
        IUnitOfWork unitOfWork,
        ILlmProvider llmProvider,
        IAutomationPlannerService automationPlanner,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        INotificationService? notificationService = null,
        IAuthorizationService? authorizationService = null,
        ILlmQuotaService? quotaService = null,
        ILlmKillSwitchService? killSwitchService = null,
        IBoardContextBuilder? boardContextBuilder = null)
    {
        _unitOfWork = unitOfWork;
        _llmProvider = llmProvider;
        _automationPlanner = automationPlanner;
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _notificationService = notificationService ?? NoOpNotificationService.Instance;
        _authorizationService = authorizationService;
        _quotaService = quotaService;
        _killSwitchService = killSwitchService;
        _boardContextBuilder = boardContextBuilder;
    }

    public async Task<Result<ChatSessionDto>> CreateSessionAsync(Guid userId, CreateChatSessionDto dto, CancellationToken ct = default)
    {
        try
        {
            var session = new ChatSession(userId, dto.Title, dto.BoardId);
            await _unitOfWork.ChatSessions.AddAsync(session, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(MapSessionToDto(session));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ChatSessionDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ChatSessionDto>> GetSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        var session = await _unitOfWork.ChatSessions.GetByIdWithMessagesAsync(sessionId, ct);
        if (session == null)
            return Result.Failure<ChatSessionDto>(ErrorCodes.NotFound, $"Chat session with ID {sessionId} not found");
        if (session.UserId != userId)
            return Result.Failure<ChatSessionDto>(ErrorCodes.Forbidden, "You do not have access to this chat session");
        return Result.Success(MapSessionToDto(session));
    }

    public async Task<Result<IEnumerable<ChatSessionDto>>> GetUserSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        var sessions = await _unitOfWork.ChatSessions.GetByUserIdAsync(userId, cancellationToken: ct);
        return Result.Success(sessions.Select(MapSessionToDto));
    }

    public async Task<ChatProviderHealthDto> GetProviderHealthAsync(bool probe = false, CancellationToken ct = default)
    {
        var health = probe
            ? await _llmProvider.ProbeAsync(ct)
            : await _llmProvider.GetHealthAsync(ct);

        return new ChatProviderHealthDto(
            health.IsAvailable,
            health.ProviderName,
            health.ErrorMessage,
            health.Model,
            health.IsMock,
            health.IsProbed);
    }

    public async Task<Result<ChatMessageDto>> SendMessageAsync(Guid sessionId, Guid userId, SendChatMessageDto dto, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Content))
                return Result.Failure<ChatMessageDto>(ErrorCodes.ValidationError, "Message content cannot be empty");
            if (dto.Content.Length > MaxPromptLength)
                return Result.Failure<ChatMessageDto>(ErrorCodes.ValidationError, $"Message exceeds max length of {MaxPromptLength} characters");

            var session = await _unitOfWork.ChatSessions.GetByIdWithMessagesAsync(sessionId, ct);
            if (session == null)
                return Result.Failure<ChatMessageDto>(ErrorCodes.NotFound, $"Chat session with ID {sessionId} not found");
            if (session.UserId != userId)
                return Result.Failure<ChatMessageDto>(ErrorCodes.Forbidden, "You do not have access to this chat session");

            // Add user message
            var userMessage = new ChatMessage(sessionId, ChatMessageRole.User, dto.Content);
            session.AddMessage(userMessage);
            await _unitOfWork.ChatMessages.AddAsync(userMessage, ct);

            var mentionResult = await PublishMentionNotificationsAsync(session, userId, dto.Content, userMessage.Id, ct);
            if (!mentionResult.IsSuccess)
                return Result.Failure<ChatMessageDto>(mentionResult.ErrorCode, mentionResult.ErrorMessage);

            if (ContainsBlockedPromptPattern(dto.Content))
            {
                var blockedMessage = new ChatMessage(
                    sessionId,
                    ChatMessageRole.Assistant,
                    "This request was blocked by safety guardrails. Rephrase with a clear board-scoped action.",
                    messageType: "error");
                session.AddMessage(blockedMessage);
                await _unitOfWork.ChatMessages.AddAsync(blockedMessage);
                await _unitOfWork.SaveChangesAsync(ct);
                return Result.Success(MapMessageToDto(blockedMessage));
            }

            // Determine message type and optional proposal attachment
            var messageType = "text";
            Guid? proposalId = null;
            string assistantContent;
            int? tokenUsage = null;
            string? degradedReason = null;

            // Quota and kill switch gate — block before any LLM call
            if (_killSwitchService != null && await _killSwitchService.IsKilledAsync(Domain.Enums.LlmSurface.Chat, userId, ct))
                return Result.Failure<ChatMessageDto>(ErrorCodes.LlmKillSwitchActive, "LLM access is currently disabled");

            if (_quotaService != null)
            {
                var quotaCheck = await _quotaService.CheckQuotaAsync(userId, Domain.Enums.LlmSurface.Chat, ct);
                if (!quotaCheck.Allowed)
                    return Result.Failure<ChatMessageDto>(ErrorCodes.LlmQuotaExceeded, quotaCheck.DeniedReason ?? "LLM quota exceeded");
            }

            if (dto.RequestProposal && LooksLikeChecklistBootstrapRequest(dto.Content))
            {
                if (!session.BoardId.HasValue)
                {
                    messageType = "error";
                    assistantContent = "Checklist bootstrap requires a board-scoped chat session. Create a session with BoardId and retry.";
                }
                else
                {
                    var bootstrapResult = await CreateChecklistBootstrapProposalAsync(
                        dto.Content,
                        userId,
                        session.BoardId.Value,
                        ct);

                    if (bootstrapResult.IsSuccess)
                    {
                        messageType = "proposal-reference";
                        proposalId = bootstrapResult.Value.Id;
                        assistantContent = $"Checklist bootstrap proposal created: {bootstrapResult.Value.Id}";
                    }
                    else
                    {
                        messageType = "error";
                        assistantContent = $"I could not create a checklist bootstrap proposal: {bootstrapResult.ErrorMessage}";
                    }
                }
            }
            else
            {
                // Get LLM response for non-checklist messages.
                var chatMessages = session.Messages
                    .Select(m => new ChatCompletionMessage(m.Role.ToString(), m.Content))
                    .ToList();

                // Build board context for board-scoped sessions
                var boardContext = await BuildBoardContextForSessionAsync(session, ct);

                var completionRequest = new ChatCompletionRequest(
                    chatMessages,
                    Attribution: BuildAttribution(session, userId),
                    BoardContext: boardContext);
                var llmResult = await _llmProvider.CompleteAsync(completionRequest, ct);
                assistantContent = llmResult.Content;
                tokenUsage = llmResult.TokensUsed;
                degradedReason = llmResult.DegradedReason;

                // Record usage for quota tracking. The provider reports a combined
                // TokensUsed total without an input/output split. Record the full
                // total as input tokens and 0 for output until providers surface
                // separate counts.
                if (_quotaService != null && llmResult.TokensUsed > 0)
                {
                    await _quotaService.RecordUsageAsync(
                        userId, Domain.Enums.LlmSurface.Chat,
                        llmResult.Provider, llmResult.Model,
                        llmResult.TokensUsed, 0,
                        ct);
                }

                if (llmResult.IsDegraded)
                {
                    messageType = "degraded";
                }

                var shouldAttemptProposal = llmResult.IsActionable || (dto.RequestProposal && session.BoardId.HasValue);

                if (shouldAttemptProposal)
                {
                    if (!session.BoardId.HasValue)
                    {
                        // Surface a hint so the user knows why no proposal was created
                        assistantContent = $"{llmResult.Content}\n\n(To act on this, open a board-scoped chat session.)";
                        messageType = "status";
                    }
                    else
                    {
                        // Determine which instructions to parse: prefer LLM-extracted
                        // instructions over the raw user message (static classifier fallback).
                        var instructionsToParse = llmResult.Instructions is { Count: > 0 }
                            ? llmResult.Instructions
                            : new List<string> { dto.Content };

                        // Use batch parsing for multiple instructions to create a single
                        // atomic proposal. For single instructions, use the original
                        // single-instruction parser for backward compatibility.
                        Result<ProposalDto>? proposalResult;
                        if (instructionsToParse.Count > 1)
                        {
                            proposalResult = await _automationPlanner.ParseBatchInstructionAsync(
                                instructionsToParse,
                                userId,
                                session.BoardId,
                                ct,
                                sourceType: ProposalSourceType.Chat,
                                sourceReferenceId: session.Id.ToString());
                        }
                        else
                        {
                            proposalResult = await _automationPlanner.ParseInstructionAsync(
                                instructionsToParse[0],
                                userId,
                                session.BoardId,
                                ct,
                                sourceType: ProposalSourceType.Chat,
                                sourceReferenceId: session.Id.ToString());
                        }

                        if (proposalResult.IsSuccess)
                        {
                            messageType = "proposal-reference";
                            proposalId = proposalResult.Value.Id;
                            assistantContent = $"{llmResult.Content}\n\nProposal created for review: {proposalResult.Value.Id}";
                        }
                        else
                        {
                            if (proposalResult.ErrorMessage?.Contains(AutomationPlannerService.ParseHintMarker) == true)
                            {
                                var hintContext = llmResult.IsActionable
                                    ? "I detected a task request but could not parse it into a proposal."
                                    : "Could not create the requested proposal.";
                                assistantContent = $"{llmResult.Content}\n\n{hintContext}\n{proposalResult.ErrorMessage}";
                                messageType = "parse-hint";
                            }
                            else if (llmResult.IsActionable)
                            {
                                assistantContent = $"{llmResult.Content}\n\n(I detected a task request but could not parse it into a proposal: {proposalResult.ErrorMessage})";
                                messageType = "status";
                            }
                            else
                            {
                                assistantContent = $"{llmResult.Content}\n\n(Could not create the requested proposal: {proposalResult.ErrorMessage})";
                                messageType = "status";
                            }
                        }
                    }
                }
            }

            var persistedDegradedReason = string.IsNullOrWhiteSpace(degradedReason)
                ? null
                : degradedReason;

            // Add assistant message
            var assistantMessage = new ChatMessage(
                sessionId,
                ChatMessageRole.Assistant,
                assistantContent,
                messageType,
                proposalId,
                tokenUsage,
                persistedDegradedReason);
            session.AddMessage(assistantMessage);
            await _unitOfWork.ChatMessages.AddAsync(assistantMessage, ct);

            await _unitOfWork.SaveChangesAsync(ct);

            return Result.Success(MapMessageToDto(assistantMessage));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ChatMessageDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async IAsyncEnumerable<LlmTokenEvent> StreamResponseAsync(Guid sessionId, Guid userId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await _unitOfWork.ChatSessions.GetByIdWithMessagesAsync(sessionId, ct);
        if (session == null || session.UserId != userId)
            yield break;

        // Kill switch and quota gate for streaming
        if (_killSwitchService != null && await _killSwitchService.IsKilledAsync(Domain.Enums.LlmSurface.Chat, userId, ct))
        {
            yield return new LlmTokenEvent(string.Empty, true, Error: "LLM access is currently disabled");
            yield break;
        }

        if (_quotaService != null)
        {
            var quotaCheck = await _quotaService.CheckQuotaAsync(userId, Domain.Enums.LlmSurface.Chat, ct);
            if (!quotaCheck.Allowed)
            {
                yield return new LlmTokenEvent(string.Empty, true, Error: quotaCheck.DeniedReason ?? "LLM quota exceeded");
                yield break;
            }
        }

        var chatMessages = session.Messages
            .Select(m => new ChatCompletionMessage(m.Role.ToString(), m.Content))
            .ToList();

        // Build board context for board-scoped sessions
        var boardContext = await BuildBoardContextForSessionAsync(session, ct);

        var request = new ChatCompletionRequest(
            chatMessages,
            Attribution: BuildAttribution(session, userId),
            BoardContext: boardContext);

        // TODO: Streaming responses cannot record usage because token counts are
        // not available until the stream completes. Implement post-stream usage
        // recording when the provider surfaces cumulative token counts.
        await foreach (var token in _llmProvider.StreamAsync(request, ct))
        {
            yield return token;
        }
    }

    private static bool ContainsBlockedPromptPattern(string content)
    {
        var normalized = content.ToLowerInvariant();
        return PromptInjectionDenylist.Any(pattern => normalized.Contains(pattern, StringComparison.Ordinal));
    }

    private async Task<string?> BuildBoardContextForSessionAsync(ChatSession session, CancellationToken ct)
    {
        if (_boardContextBuilder == null || session.BoardId == null) return null;
        return await _boardContextBuilder.BuildContextAsync(session.BoardId.Value, ct);
    }

    private static LlmRequestAttribution BuildAttribution(ChatSession session, Guid userId)
    {
        return new LlmRequestAttribution(
            userId,
            LlmRequestAttributionMapper.ResolveCorrelationIdFromActivity(),
            LlmRequestSourceSurface.Chat,
            session.BoardId,
            session.Id);
    }

    private static bool LooksLikeChecklistBootstrapRequest(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        return Regex.IsMatch(content, @"(?m)^\s*[-*]\s*\[\s\]\s+.+$");
    }

    private async Task<Result> PublishMentionNotificationsAsync(
        ChatSession session,
        Guid senderUserId,
        string content,
        Guid userMessageId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Result.Success();

        var usernames = MentionRegex
            .Matches(content)
            .Select(match => match.Groups["username"].Value)
            .Where(username => !string.IsNullOrWhiteSpace(username))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (usernames.Length == 0)
            return Result.Success();

        var sender = await _unitOfWork.Users.GetByIdAsync(senderUserId, ct);
        var senderName = sender?.Username ?? "A teammate";

        foreach (var username in usernames)
        {
            var mentionedUser = await _unitOfWork.Users.GetByUsernameAsync(username, ct);
            if (mentionedUser == null || mentionedUser.Id == senderUserId)
                continue;

            if (!await CanReceiveBoardScopedMentionAsync(mentionedUser.Id, session.BoardId, ct))
                continue;

            var publishResult = await _notificationService.PublishAsync(
                new CreateNotificationRequestDto(
                    mentionedUser.Id,
                    NotificationType.Mention,
                    "You were mentioned in chat",
                    $"{senderName} mentioned you in chat session '{session.Title}'.",
                    session.BoardId,
                    SourceEntityType: "chat-message",
                    SourceEntityId: userMessageId,
                    DeduplicationKey: $"mention:{session.Id}:{userMessageId}:{mentionedUser.Id}"),
                ct);

            if (!publishResult.IsSuccess)
                return Result.Failure(publishResult.ErrorCode, publishResult.ErrorMessage);
        }

        return Result.Success();
    }

    private async Task<bool> CanReceiveBoardScopedMentionAsync(Guid userId, Guid? boardId, CancellationToken ct)
    {
        if (!boardId.HasValue)
            return true;

        if (_authorizationService is not null)
        {
            var permission = await _authorizationService.CanReadBoardAsync(userId, boardId.Value);
            return permission.IsSuccess && permission.Value;
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId.Value, ct);
        if (board is null)
            return false;

        if (board.OwnerId == userId)
            return true;

        var access = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(boardId.Value, userId, ct);
        return access is not null && access.CanRead();
    }

    private async Task<Result<ProposalDto>> CreateChecklistBootstrapProposalAsync(
        string content,
        Guid userId,
        Guid boardId,
        CancellationToken ct)
    {
        var checklistItems = ParseChecklistItems(content);
        if (checklistItems.Count == 0)
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Could not parse checklist tasks. Use Markdown checklist lines like '- [ ] Task title'.");

        if (checklistItems.Count > MaxChecklistItemCount)
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, $"Checklist exceeds maximum item count of {MaxChecklistItemCount}.");

        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId, ct))
            .OrderBy(c => c.Position)
            .ToList();

        var targetColumn = columns.FirstOrDefault();
        if (targetColumn == null)
            return Result.Failure<ProposalDto>(ErrorCodes.NotFound, "No columns found in board for checklist bootstrap.");

        var operations = new List<CreateProposalOperationDto>();
        var sequence = 0;
        foreach (var title in checklistItems)
        {
            var parameters = JsonSerializer.Serialize(new
            {
                title,
                description = (string?)null,
                columnId = targetColumn.Id,
                boardId
            });

            operations.Add(new CreateProposalOperationDto(
                sequence++,
                "create",
                "card",
                parameters,
                Guid.NewGuid().ToString()));
        }

        var operationDtos = operations.Select(o => new ProposalOperationDto(
            Guid.NewGuid(),
            Guid.Empty,
            o.Sequence,
            o.ActionType,
            o.TargetType,
            o.TargetId,
            o.Parameters,
            o.IdempotencyKey,
            o.ExpectedVersion)).ToList();

        var permissionResult = await _policyEngine.ValidatePermissionsAsync(userId, boardId, operationDtos, ct);
        if (!permissionResult.IsSuccess)
            return Result.Failure<ProposalDto>(permissionResult.ErrorCode, permissionResult.ErrorMessage);

        var riskLevel = _policyEngine.ClassifyRisk(operationDtos);
        var summary = $"Bootstrap board from checklist ({checklistItems.Count} task{(checklistItems.Count == 1 ? string.Empty : "s")})";
        var createDto = new CreateProposalDto(
            ProposalSourceType.Chat,
            userId,
            summary,
            riskLevel,
            Guid.NewGuid().ToString(),
            boardId,
            null,
            1440,
            operations);

        var proposalResult = await _proposalService.CreateProposalAsync(createDto, ct);
        if (!proposalResult.IsSuccess)
            return Result.Failure<ProposalDto>(proposalResult.ErrorCode, proposalResult.ErrorMessage);

        return Result.Success(proposalResult.Value);
    }

    private static List<string> ParseChecklistItems(string content)
    {
        var items = new List<string>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^\s*[-*]\s*\[\s\]\s+(.+?)\s*$");
            if (!match.Success)
                continue;

            var title = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(title))
                items.Add(title);
        }

        return items;
    }

    private static ChatSessionDto MapSessionToDto(ChatSession session)
    {
        return new ChatSessionDto(
            session.Id,
            session.UserId,
            session.BoardId,
            session.Title,
            session.Status,
            session.CreatedAt,
            session.UpdatedAt,
            session.Messages.Select(MapMessageToDto).ToList()
        );
    }

    private static ChatMessageDto MapMessageToDto(ChatMessage message)
    {
        return new ChatMessageDto(
            message.Id,
            message.SessionId,
            message.Role,
            message.Content,
            message.MessageType,
            message.ProposalId,
            message.TokenUsage,
            message.CreatedAt,
            message.DegradedReason
        );
    }
}
