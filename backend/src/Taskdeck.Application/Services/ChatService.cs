using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ChatService : IChatService
{
    private const int MaxPromptLength = 4000;
    private const int MaxStreamedAssistantBytes = 1_048_576;
    private const int MaxChecklistItemCount = 30;
    private const string StreamErrorPlaceholder = "The provider could not complete the response.";
    private const string StreamErrorDegradedReason = "The upstream provider could not complete the response.";
    // Stand-in text for a provider outcome that carries no assistant text. It is deliberately generic:
    // the OpenAI-compatible buffered path sanitizes content_filter/refusal responses to empty content
    // precisely so upstream refusal text never reaches a log, a persisted row, or the client (#1617).
    private const string EmptyAssistantContentPlaceholder = "The provider ended the response without returning text.";
    private const string EmptyAssistantContentDegradedReason = "The provider returned no assistant text.";
    // The honest no-board notice (#2004). Chat can only change a board by creating a proposal for
    // review, and a proposal needs a bound board, so a session with none must say plainly that the
    // text above changed nothing — otherwise generated Markdown reads as completed work. The claim
    // is scoped to boards on purpose: the same turn does persist chat messages and any @mention
    // notifications, so an unqualified "nothing was created" would be literally false.
    private const string NoBoardActionNotice =
        "(No board is linked to this chat session, so nothing was created or changed on any board. " +
        "Select a writable board below to keep this instruction and turn it into a proposal you can review.)";
    private const string NoProposalActionNotice =
        "(No proposal was created from this attempt, so nothing changed on the board.)";
    private const string BoardCreateGuidance =
        "Chat cannot create a board in this flow. Create the board from Boards, then link this session " +
        "to that writable board and continue the instruction. No board was created or changed.";
    private const string BoardAccessDeniedMessage = "You do not have access to this board";
    private const string BoardNotFoundMessage = "Board not found";
    private const string SessionNotFoundMessage = "Chat session not found";
    private const string ArchivedBoardBindingMessage =
        "Cannot link a chat session to an archived board. Restore the board first.";
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
    private readonly ToolCallingChatOrchestrator? _toolCallingOrchestrator;
    private readonly LlmToolCallingSettings _toolCallingSettings;
    private readonly ILogger<ChatService>? _logger;
    private readonly RetiredLlmProviderConfigurationNotice? _retiredProviderNotice;

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
        IBoardContextBuilder? boardContextBuilder = null,
        ToolCallingChatOrchestrator? toolCallingOrchestrator = null,
        LlmToolCallingSettings? toolCallingSettings = null,
        ILogger<ChatService>? logger = null,
        RetiredLlmProviderConfigurationNotice? retiredProviderNotice = null)
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
        _toolCallingOrchestrator = toolCallingOrchestrator;
        _toolCallingSettings = toolCallingSettings ?? new LlmToolCallingSettings();
        _logger = logger;
        _retiredProviderNotice = retiredProviderNotice;
    }

    public async Task<Result<ChatSessionDto>> CreateSessionAsync(Guid userId, CreateChatSessionDto dto, CancellationToken ct = default)
    {
        try
        {
            var session = new ChatSession(userId, dto.Title, dto.BoardId);
            var boardAccess = await EnsureBoardReadableAsync(userId, session.BoardId);
            if (!boardAccess.IsSuccess)
                return Result.Failure<ChatSessionDto>(boardAccess.ErrorCode, boardAccess.ErrorMessage);

            await _unitOfWork.ChatSessions.AddAsync(session, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(MapSessionToDto(session));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ChatSessionDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ChatSessionDto>> BindBoardAsync(
        Guid sessionId,
        Guid userId,
        BindChatSessionBoardDto dto,
        CancellationToken ct = default)
    {
        if (dto.BoardId == Guid.Empty)
            return Result.Failure<ChatSessionDto>(ErrorCodes.ValidationError, "BoardId cannot be empty");

        var session = await _unitOfWork.ChatSessions.GetByIdWithMessagesAsync(sessionId, ct);
        if (session == null || session.UserId != userId)
            return Result.Failure<ChatSessionDto>(ErrorCodes.NotFound, SessionNotFoundMessage);

        if (session.Status == ChatSessionStatus.Archived)
            return Result.Failure<ChatSessionDto>(ErrorCodes.InvalidOperation, "Cannot bind an archived chat session");

        if (session.BoardId.HasValue && session.BoardId.Value != dto.BoardId)
        {
            return Result.Failure<ChatSessionDto>(
                ErrorCodes.Conflict,
                "This chat session is already linked to a different board. Start a new session to use another board.");
        }

        var boardAccess = await EnsureBoardWritableAsync(userId, dto.BoardId, ct);
        if (!boardAccess.IsSuccess)
            return Result.Failure<ChatSessionDto>(boardAccess.ErrorCode, boardAccess.ErrorMessage);

        if (session.BoardId == dto.BoardId)
            return Result.Success(MapSessionToDto(session));

        var boundAt = DateTimeOffset.UtcNow;
        if (await _unitOfWork.ChatSessions.TryBindBoardAsync(
                sessionId,
                userId,
                dto.BoardId,
                boundAt,
                ct))
        {
            session.BindBoard(dto.BoardId);
            return Result.Success(MapSessionToDto(session));
        }

        // Another request changed the binding after this request read the session. Reload the
        // authoritative row: the same binding is idempotent; a different binding is a conflict.
        var current = await _unitOfWork.ChatSessions.GetByIdWithMessagesAsync(sessionId, ct);
        if (current == null || current.UserId != userId)
            return Result.Failure<ChatSessionDto>(ErrorCodes.NotFound, SessionNotFoundMessage);
        if (current.BoardId == dto.BoardId)
            return Result.Success(MapSessionToDto(current));

        return Result.Failure<ChatSessionDto>(
            ErrorCodes.Conflict,
            "This chat session was linked to a different board. Start a new session to use another board.");
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
        LlmHealthStatus health;
        long? probeLatencyMs = null;
        if (probe)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                health = await _llmProvider.ProbeAsync(ct);
            }
            finally
            {
                stopwatch.Stop();
            }

            probeLatencyMs = stopwatch.ElapsedMilliseconds;
        }
        else
        {
            health = await _llmProvider.GetHealthAsync(ct);
        }

        var verificationStatus = DeriveVerificationStatus(health);

        return new ChatProviderHealthDto(
            health.IsAvailable,
            health.ProviderName,
            health.ErrorMessage,
            health.Model,
            health.IsMock,
            health.IsProbed,
            verificationStatus,
            probeLatencyMs,
            // #2233: the packaged desktop start drops retired provider variables inherited from the
            // Windows profile. Surface that here so "Verify LLM" says the settings were found and
            // ignored rather than leaving the user to wonder why their old provider is not in use.
            _retiredProviderNotice?.IgnoredEnvironmentConfiguration ?? false);
    }

    private static string DeriveVerificationStatus(LlmHealthStatus health)
    {
        if (!health.IsProbed)
            return "unverified";

        return health.IsAvailable ? "verified" : "failed";
    }

    public async Task<Result<ChatMessageDto>> SendMessageAsync(Guid sessionId, Guid userId, SendChatMessageDto dto, CancellationToken ct = default)
    {
        // Atomic quota reservation (issue #1313): reserve a slot before the LLM call, then commit it
        // with the actual token counts or release it (no usage / failure). The finally guarantees no
        // reservation leaks on any exit path, including a thrown provider error. Billed usage is
        // tracked alongside so the finally can SETTLE (commit billed tokens rather than release them)
        // when the in-try commit itself failed (#1427 review).
        Guid? quotaReservationId = null;
        var quotaCommitted = false;
        string? quotaBilledProvider = null;
        string? quotaBilledModel = null;
        var quotaBilledTokens = 0;
        var quotaEstimatedTokens = 0;
        LlmDispatchContext? quotaDispatchContext = null;
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

            var boardAccess = await EnsureBoardReadableAsync(userId, session.BoardId);
            if (!boardAccess.IsSuccess)
                return Result.Failure<ChatMessageDto>(boardAccess.ErrorCode, boardAccess.ErrorMessage);

            // Add user message
            var userMessage = new ChatMessage(sessionId, ChatMessageRole.User, dto.Content);
            session.AddMessage(userMessage);
            await _unitOfWork.ChatMessages.AddAsync(userMessage, ct);

            // The persisted message history is the clarification ledger. Once one assistant
            // clarification has a user answer, reloads still force a best-effort attempt using the
            // original intent and that answer instead of resetting the budget.
            var clarificationRounds = ClarificationDetector.CountClarificationRounds(session.Messages.ToList());
            var hasPendingClarification = HasPendingClarification(session.Messages);
            var isSkipRequest = hasPendingClarification && ClarificationDetector.IsSkipRequest(dto.Content);
            var forceBestEffort = isSkipRequest || ClarificationDetector.ShouldForceBestEffort(session.Messages.ToList());
            var actionAttemptContent = BuildActionAttemptContent(session.Messages, dto.Content, forceBestEffort);
            var (classifiedActionable, classifiedIntent) =
                LlmIntentClassifier.Classify(actionAttemptContent, _logger);
            var turnRequestsAction = dto.RequestProposal || forceBestEffort || classifiedActionable;
            var clarificationPrompt = ClarificationDetector.BuildClarificationSystemPrompt(
                clarificationRounds,
                forceBestEffort);

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
            string assistantContent = string.Empty;
            int? tokenUsage = null;
            string? degradedReason = null;
            string? toolCallMetadataJson = null;

            // Quota and kill switch gate — block before any LLM call
            if (_killSwitchService != null && await _killSwitchService.IsKilledAsync(Domain.Enums.LlmSurface.Chat, userId, ct))
                return Result.Failure<ChatMessageDto>(ErrorCodes.LlmKillSwitchActive, "LLM access is currently disabled");

            if (_quotaService != null)
            {
                var reservation = await _quotaService.ReserveAsync(userId, Domain.Enums.LlmSurface.Chat, ct);
                if (!reservation.Allowed)
                    return Result.Failure<ChatMessageDto>(ErrorCodes.LlmQuotaExceeded, reservation.DeniedReason ?? "LLM quota exceeded");
                quotaReservationId = reservation.ReservationId;
                quotaEstimatedTokens = reservation.EstimatedTokens;
            }

            if (LooksLikeChecklistBootstrapRequest(actionAttemptContent))
            {
                if (!session.BoardId.HasValue)
                {
                    messageType = "action-needs-board";
                    assistantContent = $"Checklist bootstrap needs a writable board.\n\n{NoBoardActionNotice}";
                }
                else
                {
                    var bootstrapResult = await CreateChecklistBootstrapProposalAsync(
                        actionAttemptContent,
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
                        messageType = "action-no-proposal";
                        assistantContent = $"I could not create a checklist bootstrap proposal: {bootstrapResult.ErrorMessage}\n\n{NoProposalActionNotice}";
                    }
                }
            }
            else
            {
                var usedToolCalling = false;
                LlmCompletionResult? reusableNoToolResponse = null;

                // Try tool-calling path for board-scoped sessions with orchestrator.
                // The feature flag allows disabling the orchestrator without code changes
                // (e.g. for cost control). When disabled, falls through to single-turn.
                if (_toolCallingOrchestrator != null && _toolCallingSettings.Enabled && session.BoardId.HasValue)
                {
                    var toolChatMessages = session.Messages
                        .Select(m => new ChatCompletionMessage(m.Role.ToString(), m.Content))
                        .ToList();

                    var toolCompletionRequest = new ChatCompletionRequest(
                        toolChatMessages,
                        Attribution: BuildAttribution(session, userId),
                        SystemPrompt: ToolCallingSystemPrompt.Prompt + clarificationPrompt);
                    quotaDispatchContext = toolCompletionRequest.DispatchContext;

                    var toolResult = await _toolCallingOrchestrator.ExecuteAsync(
                        toolCompletionRequest, session.BoardId.Value, userId, ct);

                    var toolCallsActuallyMade = toolResult.ToolCallLog.Count > 0;
                    // A write tool may have durably created a proposal before a later provider
                    // round fails. Its receipt is authoritative even when the closing prose is
                    // missing; falling back here could create a duplicate proposal (#2004).
                    var toolCallingUsable = !toolResult.IsDegraded
                        || toolResult.Content != null
                        || toolResult.ProposalId.HasValue;

                    if (toolCallsActuallyMade && toolCallingUsable)
                    {
                        // Tools were invoked — use the orchestrator result directly.
                        usedToolCalling = true;
                        assistantContent = toolResult.Content ?? "";
                        tokenUsage = toolResult.TokensUsed;
                        degradedReason = toolResult.DegradedReason;
                        toolCallMetadataJson = ToolCallingChatOrchestrator.BuildToolCallMetadataJson(
                            toolResult.ToolCallLog, toolResult.Rounds, toolResult.TokensUsed);

                        // Detect proposal creation from the orchestrator result.
                        // The orchestrator extracts proposal IDs from the full
                        // (un-truncated) tool results, avoiding the truncation
                        // issue in log summaries.
                        if (toolResult.ProposalId.HasValue)
                        {
                            messageType = "proposal-reference";
                            proposalId = toolResult.ProposalId.Value;
                        }
                        else if (toolResult.IsDegraded)
                        {
                            messageType = "degraded";
                            if (turnRequestsAction)
                                assistantContent = AppendNoProposalActionNotice(assistantContent);
                        }
                        else if (turnRequestsAction)
                        {
                            messageType = "action-no-proposal";
                            assistantContent = AppendNoProposalActionNotice(assistantContent);
                        }

                        // Finalize the quota reservation with the actual token count
                        if (_quotaService != null && quotaReservationId is Guid toolResId && toolResult.TokensUsed > 0)
                        {
                            // CancellationToken.None (M1, #1427 review): once billable tokens exist,
                            // finalization must not be client-cancellable — a cancelled commit would
                            // trip the finally-release and erase genuinely billed usage (quota bypass).
                            quotaBilledProvider = toolResult.Provider;
                            quotaBilledModel = toolResult.Model;
                            quotaBilledTokens = toolResult.TokensUsed;
                            await _quotaService.CommitReservationAsync(
                                toolResId,
                                userId, Domain.Enums.LlmSurface.Chat,
                                toolResult.Provider, toolResult.Model,
                                toolResult.TokensUsed, 0,
                                CancellationToken.None);
                            quotaCommitted = true;
                        }
                    }
                    else if (!toolResult.IsDegraded && !string.IsNullOrWhiteSpace(toolResult.Content))
                    {
                        // The LLM responded with plain text (no tool calls). Reuse
                        // this content instead of making a redundant CompleteAsync
                        // call (#672). The response still flows through proposal
                        // creation logic below.
                        //
                        // Run the local intent classifier on the user message so
                        // that proposal creation is still triggered for actionable
                        // messages (e.g. "create card X"). This is the same
                        // classifier all providers use as a fallback.
                        List<string>? classifiedInstructions = null;
                        if (classifiedActionable)
                        {
                            var extracted = NaturalLanguageInstructionExtractor.Extract(
                                actionAttemptContent, classifiedIntent);
                            if (extracted.Count > 0)
                                classifiedInstructions = extracted;
                        }

                        reusableNoToolResponse = new LlmCompletionResult(
                            Content: toolResult.Content,
                            TokensUsed: toolResult.TokensUsed,
                            IsActionable: classifiedActionable,
                            ActionIntent: classifiedIntent,
                            Provider: toolResult.Provider,
                            Model: toolResult.Model,
                            IsDegraded: false,
                            Instructions: classifiedInstructions);

                        // Finalize the quota reservation for the already-made call
                        if (_quotaService != null && quotaReservationId is Guid reuseResId && toolResult.TokensUsed > 0)
                        {
                            // CancellationToken.None (M1, #1427 review): see the tool-result commit above.
                            quotaBilledProvider = toolResult.Provider;
                            quotaBilledModel = toolResult.Model;
                            quotaBilledTokens = toolResult.TokensUsed;
                            await _quotaService.CommitReservationAsync(
                                reuseResId,
                                userId, Domain.Enums.LlmSurface.Chat,
                                toolResult.Provider, toolResult.Model,
                                toolResult.TokensUsed, 0,
                                CancellationToken.None);
                            quotaCommitted = true;
                        }
                    }
                    // else: degraded with null content — fall through to single-turn
                }

                // Single-turn fallback (no tool calling, tool calling degraded, or
                // reusing the no-tool response from the orchestrator).
                if (!usedToolCalling)
                {
                    LlmCompletionResult llmResult;

                    if (reusableNoToolResponse != null)
                    {
                        // Reuse the text response from the orchestrator's first LLM call
                        // instead of making a second call (#672).
                        llmResult = reusableNoToolResponse;
                    }
                    else
                    {
                        // No orchestrator response available — make a single-turn call.
                        var chatMessages = session.Messages
                            .Select(m => new ChatCompletionMessage(m.Role.ToString(), m.Content))
                            .ToList();

                        // Build board context for board-scoped sessions
                        var boardContext = await BuildBoardContextForSessionAsync(session, ct);

                        var completionRequest = new ChatCompletionRequest(
                            chatMessages,
                            Attribution: BuildAttribution(session, userId),
                            BoardContext: boardContext,
                            SystemPrompt: clarificationPrompt);
                        quotaDispatchContext = completionRequest.DispatchContext;
                        llmResult = await _llmProvider.CompleteAsync(completionRequest, ct);

                        // Finalize with authoritative combined usage when present. If the provider
                        // omitted usage, commit the reservation estimate so a short output cannot
                        // undercharge a large input. Record the chosen total as input tokens and 0
                        // for output until providers surface a reliable split.
                        var quotaTokens = ResolveQuotaTokens(
                            llmResult,
                            quotaEstimatedTokens,
                            completionRequest.DispatchContext);
                        if (_quotaService != null && quotaReservationId is Guid singleResId && quotaTokens > 0)
                        {
                            // CancellationToken.None (M1, #1427 review): see the tool-result commit above.
                            quotaBilledProvider = llmResult.Provider;
                            quotaBilledModel = llmResult.Model;
                            quotaBilledTokens = quotaTokens;
                            await _quotaService.CommitReservationAsync(
                                singleResId,
                                userId, Domain.Enums.LlmSurface.Chat,
                                llmResult.Provider, llmResult.Model,
                                quotaTokens, 0,
                                CancellationToken.None);
                            quotaCommitted = true;
                        }
                    }

                    assistantContent = llmResult.Content;
                    tokenUsage = llmResult.HasAuthoritativeTokenUsage ? llmResult.TokensUsed : null;
                    degradedReason = llmResult.DegradedReason;

                    if (llmResult.IsDegraded)
                    {
                        messageType = "degraded";
                    }

                    // Clarification loop: if the LLM response is a clarification question
                    // (either via the IsClarificationRequest flag from the provider, or
                    // detected heuristically), set the message type to "clarification"
                    // instead of attempting proposal creation.
                    var isClarification = !forceBestEffort &&
                        (llmResult.IsClarificationRequest
                         || (!llmResult.IsActionable
                             && ClarificationDetector.IsClarificationResponse(llmResult.Content)));

                    if (isClarification && messageType != "degraded")
                    {
                        messageType = "clarification";
                        // No proposal creation — wait for user's clarifying response.
                        // An unbound session cannot act, so say so here instead of letting the
                        // clarification loop end in prose with no signal (#2004).
                        // This site is deliberately NOT gated on TurnRequestsAction. `isClarification`
                        // above is decided by a text heuristic over the model's *reply* — with a live
                        // provider, IsClarificationRequest is never set, so two question marks are
                        // enough — and that says nothing about the user's intent either way. Gating
                        // here would silence exactly the turns that need the notice most: an ambiguous
                        // ask like "help me plan the sprint" carries no noun the local classifier
                        // recognises. Over-firing costs one sentence on a chatty reply that happened to
                        // end in questions; under-firing restores the silent failure this fixes.
                        // The type stays "clarification" so the round still counts toward best-effort
                        // forcing and the composer still offers the skip action. A textless
                        // clarification is left alone so it keeps falling through to the empty-content
                        // placeholder below, which deliberately reclassifies it as "degraded".
                        if (!session.BoardId.HasValue && !string.IsNullOrWhiteSpace(llmResult.Content))
                            assistantContent = AppendNoBoardActionNotice(llmResult.Content);
                    }
                    else
                    {
                        var hasProposalIntent = llmResult.IsActionable || turnRequestsAction;
                        var shouldAttemptProposal = !llmResult.IsDegraded && hasProposalIntent;

                        if (llmResult.IsDegraded && hasProposalIntent)
                        {
                            assistantContent = "The provider response was degraded. No proposal was created, and nothing changed.";
                        }
                        else if (hasProposalIntent && classifiedIntent == "board.create")
                        {
                            assistantContent = $"{llmResult.Content}\n\n{BoardCreateGuidance}";
                            messageType = "action-no-proposal";
                        }
                        else if (shouldAttemptProposal)
                        {
                            if (!session.BoardId.HasValue)
                            {
                                // Surface a hint so the user knows why no proposal was created
                                assistantContent = AppendNoBoardActionNotice(llmResult.Content);
                                messageType = "action-needs-board";
                            }
                            else
                            {
                                // Determine which instructions to parse: prefer LLM-extracted
                                // instructions over the raw user message (static classifier fallback).
                                var instructionsToParse = ResolveInstructionsToParse(
                                    llmResult,
                                    actionAttemptContent);

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
                                        assistantContent = $"{llmResult.Content}\n\n{hintContext}\n{proposalResult.ErrorMessage}\n\n{NoProposalActionNotice}";
                                        messageType = "parse-hint";
                                    }
                                    else if (llmResult.IsActionable)
                                    {
                                        assistantContent = $"{llmResult.Content}\n\n(I detected a task request but could not parse it into a proposal: {proposalResult.ErrorMessage})\n\n{NoProposalActionNotice}";
                                        messageType = "action-no-proposal";
                                    }
                                    else
                                    {
                                        assistantContent = $"{llmResult.Content}\n\n(Could not create the requested proposal: {proposalResult.ErrorMessage})\n\n{NoProposalActionNotice}";
                                        messageType = "action-no-proposal";
                                    }
                                }
                            }
                        }
                        else if (!session.BoardId.HasValue
                                 && messageType != "degraded"
                                 && !string.IsNullOrWhiteSpace(llmResult.Content)
                                 && turnRequestsAction)
                        {
                            // The provider answered in prose without flagging its own response
                            // actionable, but the turn did ask for an action: an explicit proposal
                            // request, a skip phrase such as "just do your best" (ClarificationDetector
                            // matches those anywhere, with or without a prior clarification round), or
                            // a user message the local classifier reads as a board action. With no
                            // bound board none of it could have become a proposal, so the turn says
                            // so rather than ending in Markdown that reads as completed work
                            // (#2004). A degraded turn keeps its own classification and reason, and
                            // a textless turn keeps falling through to the empty-content
                            // placeholder below — there is no prose there to be misread.
                            assistantContent = AppendNoBoardActionNotice(llmResult.Content);
                            messageType = "action-needs-board";
                        }
                    }
                }
            }

            // A provider outcome can legitimately carry no assistant text. The OpenAI-compatible
            // buffered path sanitizes content_filter and refusal responses to empty content so no
            // upstream refusal text is surfaced, and a tool-calling result can be textless too.
            // ChatMessage rejects empty content, so persisting it verbatim throws and the user loses
            // the assistant-history entry entirely (#1617). Substitute the same non-secret placeholder
            // the streaming path already uses; the sanitized specifics stay in DegradedReason.
            if (string.IsNullOrWhiteSpace(assistantContent))
            {
                assistantContent = EmptyAssistantContentPlaceholder;

                // Substituting the placeholder must not destroy a real artifact reference. When a
                // proposal was actually created (board-scoped tool calling can return a proposal id
                // with no closing text), the message stays "proposal-reference": the client renders
                // the "Open in Review" action only for that type paired with a proposalId, so
                // reclassifying here would strand a live proposal with no way to reach it.
                // "clarification" deliberately gets no such guard — a clarification with no question
                // text has nothing for the user to answer and no attached artifact, so "degraded" is
                // the honest classification for it.
                if (proposalId == null)
                    messageType = "degraded";

                if (string.IsNullOrWhiteSpace(degradedReason))
                    degradedReason = EmptyAssistantContentDegradedReason;
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

            if (toolCallMetadataJson != null)
            {
                assistantMessage.SetToolCallMetadataJson(toolCallMetadataJson);
            }

            session.AddMessage(assistantMessage);
            await _unitOfWork.ChatMessages.AddAsync(assistantMessage, ct);

            await _unitOfWork.SaveChangesAsync(ct);

            return Result.Success(MapMessageToDto(assistantMessage));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ChatMessageDto>(ex.ErrorCode, ex.Message);
        }
        finally
        {
            // Settle any reservation that was never committed (#1427 review): if billed tokens exist
            // (the in-try commit itself failed on a DB fault), COMMIT them — releasing would erase real
            // usage; otherwise release (no-LLM paths, a zero-token call, a pre-billing exception) so the
            // slot consumes no quota. CancellationToken.None so cleanup runs under a cancelled request
            // token; try/catch so a settle failure cannot mask the original exception.
            if (_quotaService != null && quotaReservationId is Guid rid && !quotaCommitted)
            {
                try
                {
                    if (quotaBilledTokens > 0 && quotaBilledProvider != null && quotaBilledModel != null)
                    {
                        await _quotaService.CommitReservationAsync(
                            rid,
                            userId, Domain.Enums.LlmSurface.Chat,
                            quotaBilledProvider, quotaBilledModel,
                            quotaBilledTokens, 0,
                            CancellationToken.None);
                    }
                    else
                    {
                        var dispatch = quotaDispatchContext?.ReadSnapshot();
                        if (dispatch is { Phase: LlmDispatchPhase.Dispatched } && quotaEstimatedTokens > 0)
                        {
                            await _quotaService.CommitReservationAsync(
                                rid,
                                userId, Domain.Enums.LlmSurface.Chat,
                                dispatch.Value.Provider ?? string.Empty,
                                dispatch.Value.Model ?? string.Empty,
                                quotaEstimatedTokens, 0,
                                CancellationToken.None);
                        }
                        else
                        {
                            await _quotaService.ReleaseReservationAsync(rid, CancellationToken.None);
                        }
                    }
                }
                catch (Exception settleEx)
                {
                    _logger?.LogError(
                        settleEx,
                        "Quota reservation {ReservationId} settle failed in SendMessageAsync (billed tokens: {Tokens}); " +
                        "the row stays Reserved until the TTL sweep.",
                        rid,
                        quotaBilledTokens);
                }
            }
        }
    }

    public async IAsyncEnumerable<LlmTokenEvent> StreamResponseAsync(Guid sessionId, Guid userId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await _unitOfWork.ChatSessions.GetByIdWithMessagesAsync(sessionId, ct);
        if (session == null || session.UserId != userId)
            yield break;

        var lastUserMessage = session.Messages.LastOrDefault(message => message.Role == ChatMessageRole.User);
        var streamRequestsAction = false;
        var streamNeedsBoard = false;
        string? streamOutcomeSuffix = null;
        if (lastUserMessage != null)
        {
            var forceBestEffort = ClarificationDetector.ShouldForceBestEffort(session.Messages.ToList());
            var actionAttemptContent = BuildActionAttemptContent(
                session.Messages,
                lastUserMessage.Content,
                forceBestEffort);
            var (isActionable, intent) = LlmIntentClassifier.Classify(actionAttemptContent, _logger);
            streamRequestsAction = forceBestEffort || isActionable;
            if (streamRequestsAction)
            {
                streamNeedsBoard = intent != "board.create" && !session.BoardId.HasValue;
                streamOutcomeSuffix = intent == "board.create"
                    ? BoardCreateGuidance
                    : session.BoardId.HasValue
                        ? NoProposalActionNotice
                        : NoBoardActionNotice;
            }
        }

        var boardAccess = await EnsureBoardReadableAsync(userId, session.BoardId);
        if (!boardAccess.IsSuccess)
        {
            yield return new LlmTokenEvent(string.Empty, true, Error: boardAccess.ErrorMessage);
            yield break;
        }

        // Kill switch and quota gate for streaming
        if (_killSwitchService != null && await _killSwitchService.IsKilledAsync(Domain.Enums.LlmSurface.Chat, userId, ct))
        {
            yield return new LlmTokenEvent(string.Empty, true, Error: "LLM access is currently disabled");
            yield break;
        }

        // Atomic quota reservation (issue #1313), mirroring the non-streaming path.
        Guid? quotaReservationId = null;
        var quotaCommitted = false;
        var quotaEstimatedTokens = 0;
        if (_quotaService != null)
        {
            var reservation = await _quotaService.ReserveAsync(userId, Domain.Enums.LlmSurface.Chat, ct);
            if (!reservation.Allowed)
            {
                yield return new LlmTokenEvent(string.Empty, true, Error: reservation.DeniedReason ?? "LLM quota exceeded");
                yield break;
            }
            quotaReservationId = reservation.ReservationId;
            // Kept for the abandoned-stream settle: with no final usage event, the reserved estimate
            // is the best available token count to commit.
            quotaEstimatedTokens = reservation.EstimatedTokens;
        }

        // Accumulate streamed content and capture usage from the final token event
        // so we can persist an assistant message and record quota usage after the
        // stream completes.
        var contentBuilder = new StringBuilder();
        var streamedContentBytes = 0;
        int? tokensUsed = null;
        string? provider = null;
        string? model = null;
        var streamIsDegraded = false;
        string? streamDegradedReason = null;
        var persistEmptyTerminalOutcome = false;
        var terminalHadError = false;
        var terminalEventSeen = false;
        // True once the provider has delivered at least one non-error event: the LLM call was made and
        // tokens flowed, so the reservation is billable even if the final usage event never arrives.
        var providerStreamed = false;
        LlmDispatchContext? quotaDispatchContext = null;

        // try/finally (no catch — legal around `yield` in an iterator) guarantees the reservation is
        // settled if the stream throws or yields no usable tokens. The scope opens immediately after
        // the reservation (Codex P2, #1427): request/board-context construction can throw or be
        // cancelled, and outside the try that would leak the reserved slot until the TTL sweep,
        // causing false quota denials for a call that never reached the provider.
        try
        {
            var chatMessages = session.Messages
                .Select(m => new ChatCompletionMessage(m.Role.ToString(), m.Content))
                .ToList();

            // Build board context for board-scoped sessions
            var boardContext = await BuildBoardContextForSessionAsync(session, ct);

            var request = new ChatCompletionRequest(
                chatMessages,
                Attribution: BuildAttribution(session, userId),
                BoardContext: boardContext);
            quotaDispatchContext = request.DispatchContext;

            await foreach (var token in _llmProvider.StreamAsync(request, ct))
            {
                // An error event carries no delivered tokens (the adapter surfaced a failure), so it
                // does not make the stream billable on its own.
                if (token.Error == null)
                    providerStreamed = true;

                // Capture terminal attribution and authoritative usage before enforcing the
                // local persistence cap. An oversized terminal event is still billable, even
                // though its content must never be appended or stored.
                if (token.Provider != null)
                    provider = token.Provider;
                if (token.Model != null)
                    model = token.Model;
                if (token.IsComplete)
                {
                    tokensUsed = token.TokensUsed;
                    terminalEventSeen = true;
                }

                var tokenBytes = Encoding.UTF8.GetByteCount(token.Token);
                if (tokenBytes > MaxStreamedAssistantBytes - streamedContentBytes)
                {
                    streamIsDegraded = true;
                    streamDegradedReason = "Streamed assistant response exceeded the safety limit.";
                    terminalHadError = true;
                    persistEmptyTerminalOutcome = true;
                    yield return new LlmTokenEvent(
                        streamOutcomeSuffix == null ? string.Empty : $"\n\n{streamOutcomeSuffix}",
                        true,
                        Error: streamDegradedReason,
                        TokensUsed: tokensUsed,
                        Provider: provider,
                        Model: model)
                    {
                        IsDegraded = true,
                        DegradedReason = streamDegradedReason
                    };
                    break;
                }

                var deliveredToken = token.Token;
                if (token.IsComplete && streamOutcomeSuffix != null)
                    deliveredToken = AppendOutcomeSuffix(deliveredToken, streamOutcomeSuffix);

                streamedContentBytes += tokenBytes;
                contentBuilder.Append(deliveredToken);
                if (token.IsDegraded)
                {
                    streamIsDegraded = true;
                    streamDegradedReason = token.DegradedReason ?? "Provider returned a degraded stream.";
                    if (token.IsComplete)
                        persistEmptyTerminalOutcome = true;
                }
                if (!string.IsNullOrWhiteSpace(token.Error))
                {
                    streamIsDegraded = true;
                    streamDegradedReason = StreamErrorDegradedReason;
                    terminalHadError = true;
                    if (token.IsComplete)
                        persistEmptyTerminalOutcome = true;
                }

                yield return deliveredToken == token.Token
                    ? token
                    : token with { Token = deliveredToken };
            }

            if (streamOutcomeSuffix != null && !terminalEventSeen)
            {
                var suffixToken = $"\n\n{streamOutcomeSuffix}";
                contentBuilder.Append(suffixToken);
                yield return new LlmTokenEvent(
                    suffixToken,
                    true,
                    TokensUsed: tokensUsed,
                    Provider: provider,
                    Model: model);
            }

            // Persist the streamed assistant message with token usage so the streaming
            // path is consistent with the non-streaming SendMessageAsync path.
            var streamedContent = contentBuilder.ToString();
            if (streamedContent.Length == 0 && persistEmptyTerminalOutcome)
                streamedContent = terminalHadError
                    ? StreamErrorPlaceholder
                    : EmptyAssistantContentPlaceholder;
            if (streamOutcomeSuffix != null && !streamedContent.Contains(streamOutcomeSuffix, StringComparison.Ordinal))
                streamedContent = AppendOutcomeSuffix(streamedContent, streamOutcomeSuffix);
            if (!string.IsNullOrEmpty(streamedContent))
            {
                var assistantMessage = new ChatMessage(
                    sessionId,
                    ChatMessageRole.Assistant,
                    streamedContent,
                    messageType: streamIsDegraded
                        ? "degraded"
                        : streamRequestsAction
                            ? streamNeedsBoard ? "action-needs-board" : "action-no-proposal"
                            : "text",
                    tokenUsage: tokensUsed,
                    degradedReason: streamIsDegraded ? streamDegradedReason : null);
                session.AddMessage(assistantMessage);
                await _unitOfWork.ChatMessages.AddAsync(assistantMessage, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }

            // Finalize the reservation with actual usage, matching the non-streaming path behavior.
            // CancellationToken.None (M1, #1427 review): once billable tokens exist, finalization
            // must not be client-cancellable.
            if (_quotaService != null && quotaReservationId is Guid rid && tokensUsed is > 0 && provider != null && model != null)
            {
                await _quotaService.CommitReservationAsync(
                    rid,
                    userId, Domain.Enums.LlmSurface.Chat,
                    provider, model,
                    tokensUsed.Value, 0,
                    CancellationToken.None);
                quotaCommitted = true;
            }
        }
        finally
        {
            // Settle, don't just release (M1 + P1, #1427 review): a provider-started stream is
            // billable. (a) Final usage known → commit the actuals. (b) The provider delivered at
            // least one token but the client abandoned the stream (disconnect/dispose) before the
            // final usage event → commit the reserved estimate; releasing here would let a client
            // that reads one token and disconnects run unmetered LLM calls, a quota bypass.
            // (c) The provider never delivered anything → nothing billable, release the slot.
            // try/catch so a settle failure (this finally also runs during iterator disposal)
            // cannot mask the original exception.
            if (_quotaService != null && quotaReservationId is Guid rid && !quotaCommitted)
            {
                try
                {
                    if (tokensUsed is > 0 && provider != null && model != null)
                    {
                        await _quotaService.CommitReservationAsync(
                            rid,
                            userId, Domain.Enums.LlmSurface.Chat,
                            provider, model,
                            tokensUsed.Value, 0,
                            CancellationToken.None);
                    }
                    else
                    {
                        var dispatch = quotaDispatchContext?.ReadSnapshot();
                        var shouldCommitEstimate = dispatch switch
                        {
                            { Phase: LlmDispatchPhase.Dispatched } => true,
                            { Phase: LlmDispatchPhase.ObservedPreDispatch } => false,
                            _ => providerStreamed
                        } && quotaEstimatedTokens > 0;
                        if (!shouldCommitEstimate)
                        {
                            await _quotaService.ReleaseReservationAsync(rid, CancellationToken.None);
                        }
                        else
                        {
                            // No final count exists, so the reserved estimate is committed as input tokens
                        // (output 0) — the deliberate over-count-not-bypass posture: better to charge
                        // the estimate than to let abandonment erase real usage. Unknown provider/model
                            // fall back to the repository's reservation placeholder.
                            await _quotaService.CommitReservationAsync(
                                rid,
                                userId, Domain.Enums.LlmSurface.Chat,
                                provider ?? dispatch?.Provider ?? string.Empty,
                                model ?? dispatch?.Model ?? string.Empty,
                                quotaEstimatedTokens, 0,
                                CancellationToken.None);
                        }
                    }
                }
                catch (Exception settleEx)
                {
                    _logger?.LogError(
                        settleEx,
                        "Quota reservation {ReservationId} settle failed in StreamResponseAsync (billed tokens: {Tokens}); " +
                        "the row stays Reserved until the TTL sweep.",
                        rid,
                        tokensUsed ?? 0);
                }
            }
        }
    }

    private static int ResolveQuotaTokens(
        LlmCompletionResult result,
        int reservationEstimate,
        LlmDispatchContext dispatchContext)
    {
        var dispatch = dispatchContext.ReadSnapshot();
        if (dispatch.Phase == LlmDispatchPhase.ObservedPreDispatch)
            return 0;
        if (dispatch.Phase == LlmDispatchPhase.Dispatched)
            return result.HasAuthoritativeTokenUsage && result.TokensUsed > 0
                ? result.TokensUsed
                : reservationEstimate;

        return result.HasAuthoritativeTokenUsage
            ? result.TokensUsed
            : result.ShouldSettleQuotaReservation
                ? reservationEstimate
                : 0;
    }

    /// <summary>
    /// Appends the no-board notice to assistant text so an unbound session never returns prose that
    /// reads as an applied change (#2004).
    /// </summary>
    private static string AppendNoBoardActionNotice(string content) =>
        $"{content}\n\n{NoBoardActionNotice}";

    /// <summary>
    /// Appends the server-derived outcome after provider prose so reloads retain the fact that an
    /// actionable attempt produced no proposal (#2004).
    /// </summary>
    private static string AppendNoProposalActionNotice(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return NoProposalActionNotice;
        return $"{content}\n\n{NoProposalActionNotice}";
    }

    private static string AppendOutcomeSuffix(string content, string suffix)
    {
        if (string.IsNullOrWhiteSpace(content))
            return suffix;
        return $"{content}\n\n{suffix}";
    }

    private static bool HasPendingClarification(IReadOnlyList<ChatMessage> messages)
    {
        return messages.Count >= 2
            && messages[^1].Role == ChatMessageRole.User
            && messages[^2].Role == ChatMessageRole.Assistant
            && messages[^2].MessageType == "clarification";
    }

    private static string BuildActionAttemptContent(
        IReadOnlyList<ChatMessage> messages,
        string currentAnswer,
        bool forceBestEffort)
    {
        if (!forceBestEffort || !HasPendingClarification(messages))
            return currentAnswer;

        for (var index = messages.Count - 3; index >= 0; index--)
        {
            if (messages[index].Role == ChatMessageRole.User)
                return $"{messages[index].Content}\n\nClarification answer: {currentAnswer}";
        }

        return currentAnswer;
    }

    private static IReadOnlyList<string> ResolveInstructionsToParse(
        LlmCompletionResult llmResult,
        string actionAttemptContent)
    {
        if (llmResult.Instructions is { Count: > 0 })
            return llmResult.Instructions;

        return new[] { actionAttemptContent };
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

    private async Task<Result> EnsureBoardReadableAsync(Guid userId, Guid? boardId)
    {
        if (!boardId.HasValue)
            return Result.Success();

        if (_authorizationService == null)
            return Result.Failure(ErrorCodes.Forbidden, BoardAccessDeniedMessage);

        var permission = await _authorizationService.CanReadBoardAsync(userId, boardId.Value);
        if (!permission.IsSuccess)
        {
            return permission.ErrorCode == ErrorCodes.NotFound
                ? Result.Failure(ErrorCodes.NotFound, BoardNotFoundMessage)
                : Result.Failure(permission.ErrorCode, permission.ErrorMessage);
        }

        return permission.Value
            ? Result.Success()
            : Result.Failure(ErrorCodes.NotFound, BoardNotFoundMessage);
    }

    private async Task<Result> EnsureBoardWritableAsync(
        Guid userId,
        Guid boardId,
        CancellationToken ct)
    {
        if (_authorizationService == null)
            return Result.Failure(ErrorCodes.Forbidden, BoardAccessDeniedMessage);

        var permission = await _authorizationService.CanWriteBoardAsync(userId, boardId);
        if (!permission.IsSuccess || !permission.Value)
        {
            return permission.ErrorCode == ErrorCodes.NotFound || permission.IsSuccess
                ? Result.Failure(ErrorCodes.NotFound, BoardNotFoundMessage)
                : Result.Failure(permission.ErrorCode, permission.ErrorMessage);
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId, ct);
        if (board == null)
            return Result.Failure(ErrorCodes.NotFound, BoardNotFoundMessage);
        if (board.IsArchived)
            return Result.Failure(ErrorCodes.InvalidOperation, ArchivedBoardBindingMessage);

        return Result.Success();
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

        // Creating a proposal is a mutation lane: write-capable membership required (#1836).
        var permissionResult = await _policyEngine.ValidatePermissionsAsync(userId, boardId, operationDtos, BoardAccessBar.Write, ct);
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
            message.DegradedReason,
            message.ToolCallMetadataJson
        );
    }
}
