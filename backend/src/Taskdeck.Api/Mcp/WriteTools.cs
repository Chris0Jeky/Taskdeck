using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Api.Mcp;

/// <summary>
/// MCP write tools. All write operations create automation proposals — they never
/// mutate board state directly. This preserves GP-06 (Review-First Automation Safety).
/// Each tool returns a proposal ID that the user must approve in the Review UI.
/// </summary>
[McpServerToolType]
public class WriteTools
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IUserContextProvider _userContext;
    private readonly ICaptureService _captureService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService _authorizationService;
    private readonly IBoardRelationService? _relations;

    public WriteTools(
        IAutomationProposalService proposalService,
        IUserContextProvider userContext,
        ICaptureService captureService,
        IUnitOfWork unitOfWork)
        : this(
            proposalService,
            userContext,
            captureService,
            unitOfWork,
            new AuthorizationService(unitOfWork))
    {
    }

    public WriteTools(
        IAutomationProposalService proposalService,
        IUserContextProvider userContext,
        ICaptureService captureService,
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService,
        IBoardRelationService? relations = null)
    {
        _proposalService = proposalService;
        _userContext = userContext;
        _captureService = captureService;
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
        _relations = relations;
    }

    [McpServerTool(Name = "add_card_relation"), Description(
        "Creates a proposal to add one typed relation between two active cards on the same board. Nothing changes until review, approval and Apply. The supplied expected revision is preserved exactly.")]
    public Task<string> AddCardRelation(
        string board_id,
        string card_id,
        string related_card_id,
        string relation_type,
        long expected_revision)
        => ProposeCardRelation(board_id, card_id, related_card_id, relation_type, expected_revision, remove: false);

    [McpServerTool(Name = "remove_card_relation"), Description(
        "Creates a proposal to remove one typed relation between two active cards on the same board. Nothing changes until review, approval and Apply. The supplied expected revision is preserved exactly.")]
    public Task<string> RemoveCardRelation(
        string board_id,
        string card_id,
        string related_card_id,
        string relation_type,
        long expected_revision)
        => ProposeCardRelation(board_id, card_id, related_card_id, relation_type, expected_revision, remove: true);

    /// <summary>
    /// Rejects label IDs that are not labels on the target board before a proposal is
    /// created, so an MCP caller gets immediate feedback instead of a dead proposal that
    /// only fails when the shared preview/apply contract runs. Returns an error message
    /// when any ID is unknown, otherwise null.
    /// </summary>
    private async Task<string?> ValidateBoardLabelsAsync(Guid boardId, IReadOnlyCollection<Guid> labelIds)
    {
        if (labelIds.Count == 0)
            return null;

        var boardLabelIds = (await _unitOfWork.Labels.GetByBoardIdAsync(boardId))
            .Select(label => label.Id)
            .ToHashSet();
        var missing = labelIds.Where(id => !boardLabelIds.Contains(id)).Distinct().ToList();
        return missing.Count == 0
            ? null
            : $"label_ids not found on board: {string.Join(", ", missing)}";
    }

    /// <summary>
    /// Creates a PROPOSAL to add a new card to a board. The card is NOT created
    /// immediately -- a proposal is generated that the user must review and approve
    /// in Taskdeck's Review tab before the card appears on the board.
    /// Returns the proposal ID for status tracking.
    /// </summary>
    [McpServerTool(Name = "create_card"), Description(
        "Creates a PROPOSAL to add a new card to a board. The card is NOT created immediately -- " +
        "a proposal is generated that the user must review and approve in Taskdeck's Review tab " +
        "before the card appears on the board. Returns the proposal ID for status tracking.")]
    public async Task<string> CreateCard(
        [Description("Target board ID (UUID)")]
        string board_id,
        [Description("Card title (max 200 characters)")]
        string title,
        [Description("Optional. Target column ID. If omitted, the first column is used.")]
        string? column_id = null,
        [Description("Optional. Card description in plain text.")]
        string? description = null,
        [Description("Optional. Label IDs to apply to the card (comma-separated UUIDs).")]
        string? label_ids = null,
        [Description("Optional. Due date as YYYY-MM-DD or an ISO-8601 timestamp with an explicit offset.")]
        string? due_date = null,
        [Description("Optional. Work item type: Task, Epic, or Spike. Defaults to Task.")]
        string? work_item_type = null,
        [Description("Optional. Estimated effort in whole minutes (0 to 1000000). Omitted or null means unknown; 0 is a known zero estimate.")]
        int? estimated_effort_minutes = null)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(board_id, out var boardGuid))
            return Error("Invalid board_id format");

        if (estimated_effort_minutes is < 0 or > Card.MaxEstimatedEffortMinutes)
            return Error($"estimated_effort_minutes must be between 0 and {Card.MaxEstimatedEffortMinutes}");

        var canWrite = await _authorizationService.CanWriteBoardAsync(userId, boardGuid);
        if (!canWrite.IsSuccess)
            return Error(canWrite);
        if (!canWrite.Value)
            return Error("Not authorized to create cards on this board");

        Guid? requestedColumnId = null;
        if (!string.IsNullOrWhiteSpace(column_id))
        {
            if (!Guid.TryParse(column_id, out var parsedColumnId))
                return Error("Invalid column_id format");
            requestedColumnId = parsedColumnId;
        }

        var columns = await _unitOfWork.Columns.GetByBoardIdAsync(boardGuid);
        var column = requestedColumnId.HasValue
            ? columns.SingleOrDefault(candidate => candidate.Id == requestedColumnId.Value)
            : columns.OrderBy(candidate => candidate.Position).ThenBy(candidate => candidate.Id).FirstOrDefault();
        if (column is null)
        {
            return string.IsNullOrWhiteSpace(column_id)
                ? Error("No columns found in board")
                : Error("column_id not found on board");
        }

        var parameters = new Dictionary<string, object?>
        {
            ["boardId"] = boardGuid,
            ["title"] = title,
            ["columnId"] = column.Id
        };

        if (work_item_type is not null)
        {
            if (work_item_type is not ("Task" or "Epic" or "Spike")) return Error("work_item_type must be Task, Epic, or Spike");
            parameters["workItemType"] = work_item_type;
        }
        if (estimated_effort_minutes.HasValue)
            parameters["estimatedEffortMinutes"] = estimated_effort_minutes.Value;
        if (!string.IsNullOrWhiteSpace(description))
            parameters["description"] = description;

        if (!string.IsNullOrWhiteSpace(label_ids))
        {
            if (!TryParseGuidList(label_ids, out var labelIds))
                return Error("label_ids must contain only comma-separated non-empty UUIDs");
            var labelError = await ValidateBoardLabelsAsync(boardGuid, labelIds);
            if (labelError != null)
                return Error(labelError);
            parameters["labelIds"] = labelIds;
        }

        if (due_date != null)
        {
            if (!TryParseDueDate(due_date, out var dueDate, out var dueDateError))
                return Error(dueDateError);
            parameters["dueDate"] = dueDate;
        }

        var dto = new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: $"Create card: {title}",
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardGuid,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 0,
                    ActionType: "create",
                    TargetType: "card",
                    Parameters: JsonSerializer.Serialize(parameters, BoardResources.SerializerOptions),
                    IdempotencyKey: Guid.NewGuid().ToString())
            });

        var result = await _proposalService.CreateProposalAsync(dto);
        if (!result.IsSuccess)
            return Error(result);

        return ProposalCreated(result.Value.Id, "Proposal created. Review and approve in Taskdeck to create the card.");
    }

    /// <summary>
    /// Creates a PROPOSAL to move a card to a different column. The card is NOT
    /// moved immediately -- the proposal must be approved by the user first.
    /// Returns the proposal ID.
    /// </summary>
    [McpServerTool(Name = "move_card"), Description(
        "Creates a PROPOSAL to move a card to a different column. The card is NOT moved immediately -- " +
        "the proposal must be approved by the user first. Returns the proposal ID.")]
    public async Task<string> MoveCard(
        [Description("Board ID containing the card (UUID)")]
        string board_id,
        [Description("Card ID to move (UUID)")]
        string card_id,
        [Description("Target column ID (UUID)")]
        string target_column_id)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(board_id, out var boardGuid))
            return Error("Invalid board_id format");
        if (!Guid.TryParse(card_id, out var cardGuid))
            return Error("Invalid card_id format");
        if (!Guid.TryParse(target_column_id, out var targetColumnGuid))
            return Error("Invalid target_column_id format");

        var parameters = new Dictionary<string, object>
        {
            ["boardId"] = boardGuid,
            ["cardId"] = cardGuid,
            // The proposal executor's canonical move contract is columnId. Keep
            // target_column_id as the public MCP argument, but normalize it here.
            ["columnId"] = targetColumnGuid
        };

        var dto = new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: $"Move card to new column",
            RiskLevel: RiskLevel.Medium,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardGuid,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 0,
                    ActionType: "move",
                    TargetType: "card",
                    Parameters: JsonSerializer.Serialize(parameters, BoardResources.SerializerOptions),
                    IdempotencyKey: Guid.NewGuid().ToString(),
                    TargetId: cardGuid.ToString())
            });

        var result = await _proposalService.CreateProposalAsync(dto);
        if (!result.IsSuccess)
            return Error(result);

        return ProposalCreated(result.Value.Id, "Proposal created. Review and approve in Taskdeck to move the card.");
    }

    /// <summary>
    /// Creates a PROPOSAL to update card fields (title, description, due date, labels, estimated effort).
    /// The card is NOT updated immediately -- the proposal must be approved first.
    /// Returns the proposal ID.
    /// </summary>
    [McpServerTool(Name = "update_card"), Description(
        "Creates a PROPOSAL to update card fields (title, description, due date, labels, estimated effort). " +
        "The card is NOT updated immediately -- the proposal must be approved first. " +
        "Returns the proposal ID.")]
    public async Task<string> UpdateCard(
        [Description("Board ID (UUID)")]
        string board_id,
        [Description("Card ID (UUID)")]
        string card_id,
        [Description("Optional. New title.")]
        string? title = null,
        [Description("Optional. New description.")]
        string? description = null,
        [Description("Optional. Replace label set with these IDs (comma-separated UUIDs).")]
        string? label_ids = null,
        [Description("Optional. New due date as YYYY-MM-DD or an ISO-8601 timestamp with an explicit offset.")]
        string? due_date = null,
        [Description("Optional. Set true to remove the current due date.")]
        bool clear_due_date = false,
        [Description("Optional. Work item type: Task, Epic, or Spike.")]
        string? work_item_type = null,
        [Description("Required for type, parent, or estimated effort changes. Current card updatedAt timestamp from a fresh read.")]
        string? expected_updated_at = null,
        [Description("Optional. Same-board parent card ID. Requires expected_updated_at.")] string? parent_card_id = null,
        [Description("Remove the current parent. Requires expected_updated_at.")] bool clear_parent = false,
        [Description("Optional. Estimated effort in whole minutes (0 to 1000000). Omitted or null leaves unchanged; 0 sets a known zero estimate. Requires expected_updated_at.")]
        int? estimated_effort_minutes = null,
        [Description("Optional. Set true to clear estimated effort to unknown. Cannot be combined with estimated_effort_minutes. Requires expected_updated_at.")]
        bool clear_estimated_effort = false)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(board_id, out var boardGuid))
            return Error("Invalid board_id format");
        if (!Guid.TryParse(card_id, out var cardGuid))
            return Error("Invalid card_id format");

        if (estimated_effort_minutes is < 0 or > Card.MaxEstimatedEffortMinutes)
            return Error($"estimated_effort_minutes must be between 0 and {Card.MaxEstimatedEffortMinutes}");
        if (estimated_effort_minutes.HasValue && clear_estimated_effort)
            return Error("estimated_effort_minutes and clear_estimated_effort cannot both be specified");

        if (title == null && description == null && label_ids == null && due_date == null && !clear_due_date && work_item_type == null && parent_card_id == null && !clear_parent && estimated_effort_minutes == null && !clear_estimated_effort)
            return Error("At least one card field or explicit clear action must be provided");

        var parameters = new Dictionary<string, object?>
        {
            ["boardId"] = boardGuid,
            ["cardId"] = cardGuid
        };

        if (work_item_type is not null || parent_card_id is not null || clear_parent || estimated_effort_minutes.HasValue || clear_estimated_effort)
        {
            var access = await _authorizationService.CanWriteBoardAsync(userId, boardGuid);
            if (!access.IsSuccess) return Error(access);
            if (!access.Value) return Error("Not authorized to update cards on this board");
            if (work_item_type is not null && work_item_type is not ("Task" or "Epic" or "Spike")) return Error("work_item_type must be Task, Epic, or Spike");
            if (!DateTimeOffset.TryParse(expected_updated_at, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var expected))
                return Error("expected_updated_at is required for a type, parent, or estimated effort change");
            var card = await _unitOfWork.Cards.GetByIdAsync(cardGuid);
            if (card is null || card.BoardId != boardGuid) return Error("Card not found on board");
            if (card.IsArchived || card.UpdatedAt != expected) return Error("Card is archived or changed. Refresh it before proposing a type, parent, or estimated effort change.");
            if (work_item_type is not null) parameters["workItemType"] = work_item_type;
            if (parent_card_id is not null)
            {
                if (!Guid.TryParse(parent_card_id, out var parentId) || parentId == Guid.Empty || clear_parent) return Error("Provide a valid parent_card_id or clear_parent, never both");
                parameters["parentCardId"] = parentId;
            }
            if (clear_parent) parameters["clearParent"] = true;
            parameters["expectedUpdatedAt"] = expected;
        }
        if (title != null) parameters["title"] = title;
        if (description != null) parameters["description"] = description;
        if (label_ids != null)
        {
            if (!TryParseGuidList(label_ids, out var labelIds))
                return Error("label_ids must contain only comma-separated non-empty UUIDs");
            var labelError = await ValidateBoardLabelsAsync(boardGuid, labelIds);
            if (labelError != null)
                return Error(labelError);
            parameters["labelIds"] = labelIds;
        }

        if (due_date != null)
        {
            if (!TryParseDueDate(due_date, out var dueDate, out var dueDateError))
                return Error(dueDateError);
            if (clear_due_date)
                return Error("due_date and clear_due_date cannot both be specified");
            parameters["dueDate"] = dueDate;
        }

        if (clear_due_date)
            parameters["clearDueDate"] = true;

        if (estimated_effort_minutes.HasValue)
            parameters["estimatedEffortMinutes"] = estimated_effort_minutes.Value;
        if (clear_estimated_effort)
            parameters["clearEstimatedEffort"] = true;

        var summary = title != null ? $"Update card: {title}" : "Update card fields";

        var dto = new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: summary,
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardGuid,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 0,
                    ActionType: "update",
                    TargetType: "card",
                    Parameters: JsonSerializer.Serialize(parameters, BoardResources.SerializerOptions),
                    IdempotencyKey: Guid.NewGuid().ToString(),
                    TargetId: cardGuid.ToString())
            });

        var result = await _proposalService.CreateProposalAsync(dto);
        if (!result.IsSuccess)
            return Error(result);

        return ProposalCreated(result.Value.Id, "Proposal created. Review and approve in Taskdeck to update the card.");
    }

    /// <summary>
    /// Creates a PROPOSAL to mark a card blocked. Nothing changes immediately.
    /// After explicit review and approval, Apply marks the card blocked with the
    /// generated reason "Archived by an approved proposal." Returns the proposal ID.
    /// </summary>
    [McpServerTool(Name = "archive_card"), Description(
        "Creates a PROPOSAL to mark a card blocked. Nothing changes immediately. " +
        "After explicit review and approval, Apply marks the card blocked with the generated reason " +
        "'Archived by an approved proposal.' Returns the proposal ID.")]
    public async Task<string> ArchiveCard(
        [Description("Board ID (UUID)")]
        string board_id,
        [Description("Card ID to mark blocked after approval (UUID)")]
        string card_id)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(board_id, out var boardGuid))
            return Error("Invalid board_id format");
        if (!Guid.TryParse(card_id, out var cardGuid))
            return Error("Invalid card_id format");

        var parameters = new Dictionary<string, object>
        {
            ["boardId"] = boardGuid,
            ["cardId"] = cardGuid
        };

        var dto = new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: "Archive card",
            RiskLevel: RiskLevel.High,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardGuid,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 0,
                    ActionType: "archive",
                    TargetType: "card",
                    Parameters: JsonSerializer.Serialize(parameters, BoardResources.SerializerOptions),
                    IdempotencyKey: Guid.NewGuid().ToString(),
                    TargetId: cardGuid.ToString())
            });

        var result = await _proposalService.CreateProposalAsync(dto);
        if (!result.IsSuccess)
            return Error(result);

        return ProposalCreated(
            result.Value.Id,
            "Proposal created. Review and approve in Taskdeck; Apply will mark the card blocked with reason 'Archived by an approved proposal.'");
    }

    [McpServerTool(Name = "archive_card_lifecycle"), Description(
        "Creates a PROPOSAL to archive a card in place, hiding it from active work while retaining its ID, labels and history. Requires the current card updatedAt. Explicit review, approval and Apply are required; nothing changes immediately.")]
    public Task<string> ArchiveCardLifecycle(string board_id, string card_id, string expected_updated_at, string? expected_children_fingerprint = null)
        => ProposeCardLifecycle(board_id, card_id, expected_updated_at, true, expected_children_fingerprint);

    [McpServerTool(Name = "restore_archived_card"), Description(
        "Creates a PROPOSAL to restore an archived card to its original column and position. Requires the archived card updatedAt. Explicit review, approval and Apply are required; nothing changes immediately.")]
    public Task<string> RestoreArchivedCard(string board_id, string card_id, string expected_updated_at)
        => ProposeCardLifecycle(board_id, card_id, expected_updated_at, false);

    [McpServerTool(Name = "replace_card_assignments"), Description(
        "Creates a PROPOSAL to replace a card's assignee set. Use eligible board participant UUIDs, [] to clear, and the current updatedAt. Requires explicit review, approval and Apply; never mutates the card directly.")]
    public async Task<string> ReplaceCardAssignments(string board_id, string card_id, string[] user_ids, string expected_updated_at)
    {
        var actor = await _userContext.GetCurrentUserIdAsync();
        if (!Guid.TryParse(board_id, out var boardId) || !Guid.TryParse(card_id, out var cardId) ||
            !DateTimeOffset.TryParse(expected_updated_at, out var expected) ||
            user_ids is null || user_ids.Any(id => !Guid.TryParse(id, out _)))
            return Error("Provide board_id, card_id, user_ids and expected_updated_at.");
        var permission = await _authorizationService.CanWriteBoardAsync(actor, boardId);
        if (!permission.IsSuccess || !permission.Value) return Error("Not authorized to assign this card.");
        var parameters = JsonSerializer.Serialize(new { cardId, userIds = user_ids.Select(Guid.Parse).Distinct().ToArray(), expectedUpdatedAt = expected });
        using var parsed = JsonDocument.Parse(parameters);
        var valid = await ProposalAssignmentContract.ValidateAsync(_unitOfWork, boardId, parsed.RootElement, default);
        if (!valid.IsSuccess) return Error(valid);
        var result = await _proposalService.CreateProposalAsync(new CreateProposalDto(
            ProposalSourceType.Manual, actor, valid.Value, RiskLevel.Medium, Guid.NewGuid().ToString(), boardId,
            Operations: [new(0, ProposalAssignmentContract.Action, "card", parameters, Guid.NewGuid().ToString(), cardId.ToString())]));
        return result.IsSuccess ? ProposalCreated(result.Value.Id, "Review, approve and Apply explicitly in Taskdeck.") : Error(result);
    }

    private async Task<string> ProposeCardRelation(
        string boardId,
        string cardId,
        string relatedCardId,
        string relationType,
        long expectedRevision,
        bool remove)
    {
        if (_relations is null)
            return Error("Card relations are unavailable in this host.");

        var actor = await _userContext.GetCurrentUserIdAsync();
        if (!Guid.TryParse(boardId, out var boardGuid) ||
            !Guid.TryParse(cardId, out var cardGuid) ||
            !Guid.TryParse(relatedCardId, out var relatedCardGuid) ||
            string.IsNullOrWhiteSpace(relationType))
        {
            return Error("Provide board_id, card_id, related_card_id and relation_type as valid values.");
        }

        var edge = new CardRelationEdge(cardGuid, relatedCardGuid, relationType);
        // This is the same server-side validation the proposal handler uses. It verifies
        // the trusted actor, both active endpoints, board membership and the caller-pinned
        // revision before creating a proposal; no caller-provided authority is consulted.
        var validation = await _relations.ValidateMutationAsync(
            actor, boardGuid, edge, expectedRevision, remove, CancellationToken.None);
        if (!validation.IsSuccess)
            return Error(validation);

        var parameters = JsonSerializer.Serialize(new
        {
            boardId = boardGuid,
            cardId = cardGuid,
            relatedCardId = relatedCardGuid,
            relationType,
            expectedRevision
        }, BoardResources.SerializerOptions);
        var action = remove ? "remove-relation" : "add-relation";
        var proposal = new CreateProposalDto(
            ProposalSourceType.Manual,
            actor,
            remove ? "Remove card relation" : "Add card relation",
            RiskLevel.Medium,
            Guid.NewGuid().ToString(),
            boardGuid,
            Operations: [new(0, action, "card", parameters, Guid.NewGuid().ToString(), cardGuid.ToString())]);

        var result = await _proposalService.CreateProposalAsync(proposal);
        return result.IsSuccess
            ? ProposalCreated(result.Value.Id, "Proposal created. Review, approve and Apply explicitly in Taskdeck.")
            : Error(result);
    }

    private async Task<string> ProposeCardLifecycle(string boardId, string cardId, string timestamp, bool archive, string? fingerprint = null)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();
        if (!Guid.TryParse(boardId, out var boardGuid) || !Guid.TryParse(cardId, out var cardGuid))
            return Error("Invalid board_id or card_id format");
        if (!DateTimeOffset.TryParse(timestamp, out var expected)) return Error("Invalid expected_updated_at timestamp");
        var canWrite = await _authorizationService.CanWriteBoardAsync(userId, boardGuid);
        if (!canWrite.IsSuccess)
            return Error(canWrite);
        if (!canWrite.Value)
            return Error("Not authorized to archive or restore cards on this board");
        var lifecycleParameters = new Dictionary<string, object> { ["boardId"] = boardGuid, ["cardId"] = cardGuid, ["expectedUpdatedAt"] = expected };
        if (fingerprint is not null) lifecycleParameters["expectedChildrenFingerprint"] = fingerprint;
        var parameters = JsonSerializer.Serialize(lifecycleParameters);
        var result = await _proposalService.CreateProposalAsync(new CreateProposalDto(
            SourceType: ProposalSourceType.Manual, RequestedByUserId: userId,
            Summary: archive ? "Archive card" : "Restore card", RiskLevel: RiskLevel.High,
            CorrelationId: Guid.NewGuid().ToString(), BoardId: boardGuid,
            Operations: new List<CreateProposalOperationDto> { new(0, archive ? "archive-lifecycle" : "restore-lifecycle",
                "card", parameters, Guid.NewGuid().ToString(), cardGuid.ToString()) }));
        return result.IsSuccess ? ProposalCreated(result.Value.Id, "Proposal created. Review, approve and Apply explicitly in Taskdeck.") : Error(result);
    }

    /// <summary>
    /// Captures a new item into the inbox. This is a low-risk operation -- the item is
    /// added to the inbox immediately (no proposal needed). The item can later be triaged
    /// into a board card via the review flow.
    /// </summary>
    [McpServerTool(Name = "create_capture"), Description(
        "Captures a new item into the inbox. This is a low-risk operation -- the item is added " +
        "to the inbox immediately (no proposal needed). The item can later be triaged into a " +
        "board card via the review flow.")]
    public async Task<string> CreateCapture(
        [Description("The capture text (idea, task, note)")]
        string text,
        [Description("Optional. Target board for triage.")]
        string? board_id = null)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        Guid? boardGuid = null;
        if (!string.IsNullOrWhiteSpace(board_id))
        {
            if (!Guid.TryParse(board_id, out var parsed))
                return Error("Invalid board_id format");
            boardGuid = parsed;
        }

        var captureDto = new CreateCaptureItemDto(
            BoardId: boardGuid,
            Text: text,
            Source: "Typed");

        var result = await _captureService.CreateAsync(userId, captureDto);
        if (!result.IsSuccess)
            return Error(result);

        return JsonSerializer.Serialize(new
        {
            captureId = result.Value.Id,
            status = result.Value.Status.ToString(),
            message = "Capture added to inbox. Triage via the Taskdeck inbox to convert to a board card."
        }, BoardResources.SerializerOptions);
    }

    /// <summary>
    /// Creates a PROPOSAL to add a new column to a board. The column is NOT created
    /// immediately -- a proposal is generated that the user must review and approve.
    /// Returns the proposal ID.
    /// </summary>
    [McpServerTool(Name = "create_column"), Description(
        "Creates a PROPOSAL to add a new column to a board. The column is NOT created immediately -- " +
        "a proposal is generated that the user must review and approve. Returns the proposal ID.")]
    public async Task<string> CreateColumn(
        [Description("Target board ID (UUID)")]
        string board_id,
        [Description("Column name")]
        string name,
        [Description("Optional. WIP limit for the column.")]
        int? wip_limit = null)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(board_id, out var boardGuid))
            return Error("Invalid board_id format");

        var canWrite = await _authorizationService.CanWriteBoardAsync(userId, boardGuid);
        if (!canWrite.IsSuccess)
            return Error(canWrite);
        if (!canWrite.Value)
            return Error("Not authorized to create columns on this board");

        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardGuid)).ToList();
        var appendPositionResult = ProposalOperationContractValidator.ResolveAppendPosition(columns);
        if (!appendPositionResult.IsSuccess)
            return Error(appendPositionResult);

        var parameters = new Dictionary<string, object?>
        {
            ["boardId"] = boardGuid,
            ["name"] = name,
            ["position"] = appendPositionResult.Value
        };

        if (wip_limit.HasValue)
            parameters["wipLimit"] = wip_limit.Value;

        var createOperation = new CreateProposalOperationDto(
            Sequence: 0,
            ActionType: "create",
            TargetType: "column",
            Parameters: JsonSerializer.Serialize(parameters, BoardResources.SerializerOptions),
            IdempotencyKey: Guid.NewGuid().ToString());
        var operation = new ProposalOperationDto(
            Guid.Empty,
            Guid.Empty,
            createOperation.Sequence,
            createOperation.ActionType,
            createOperation.TargetType,
            createOperation.TargetId,
            createOperation.Parameters,
            createOperation.IdempotencyKey,
            createOperation.ExpectedVersion);
        var contractValidation = await ProposalOperationContractValidator.ValidateAsync(
            _unitOfWork,
            boardGuid,
            new[] { operation });
        if (!contractValidation.IsSuccess)
            return Error(contractValidation);

        var dto = new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: $"Create column: {name}",
            RiskLevel: RiskLevel.Medium,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardGuid,
            Operations: new List<CreateProposalOperationDto>
            {
                createOperation
            });

        var result = await _proposalService.CreateProposalAsync(dto);
        if (!result.IsSuccess)
            return Error(result);

        return ProposalCreated(result.Value.Id, "Proposal created. Review and approve in Taskdeck to create the column.");
    }

    private static bool TryParseGuidList(string commaSeparated, out List<Guid> values)
    {
        values = new List<Guid>();
        if (string.IsNullOrWhiteSpace(commaSeparated))
            return true;

        var parts = commaSeparated.Split(',', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part) || !Guid.TryParse(part, out var value) || value == Guid.Empty)
                return false;
            values.Add(value);
        }

        return true;
    }

    private static bool TryParseDueDate(string raw, out string normalized, out string error)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { dueDate = raw }));
        if (!OperationParameterParser.TryGetOptionalDateTimeOffset(
                document.RootElement,
                "dueDate",
                out _,
                out var dueDate,
                out error))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = dueDate!.Value.ToString("O");
        return true;
    }

    private static string Error(string message)
    {
        return JsonSerializer.Serialize(new { error = message }, BoardResources.SerializerOptions);
    }

    /// <summary>
    /// Serializes a failed application <see cref="Result"/> for an MCP caller. A result
    /// classified <c>UnexpectedError</c> collapses to the stable generic failure message so
    /// unknown-exception text never reaches the model; known domain messages stay specific.
    /// </summary>
    private static string Error(Result result)
    {
        return Error(SensitiveDataRedactor.SanitizeLlmFailureMessage(
            result.ErrorCode,
            result.ErrorMessage));
    }

    private static string ProposalCreated(Guid proposalId, string message)
    {
        return JsonSerializer.Serialize(new
        {
            proposalId,
            status = "Pending",
            message
        }, BoardResources.SerializerOptions);
    }
}
