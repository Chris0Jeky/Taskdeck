using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
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

    public WriteTools(
        IAutomationProposalService proposalService,
        IUserContextProvider userContext,
        ICaptureService captureService)
    {
        _proposalService = proposalService;
        _userContext = userContext;
        _captureService = captureService;
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
        string? label_ids = null)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(board_id, out var boardGuid))
            return Error("Invalid board_id format");

        var parameters = new Dictionary<string, object?>
        {
            ["boardId"] = boardGuid,
            ["title"] = title
        };

        if (!string.IsNullOrWhiteSpace(column_id))
        {
            if (!Guid.TryParse(column_id, out var columnGuid))
                return Error("Invalid column_id format");
            parameters["columnId"] = columnGuid;
        }

        if (!string.IsNullOrWhiteSpace(description))
            parameters["description"] = description;

        if (!string.IsNullOrWhiteSpace(label_ids))
            parameters["labelIds"] = ParseGuidList(label_ids);

        var dto = new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: $"Create card: {title}",
            RiskLevel: RiskLevel.Medium,
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
            return Error(result.ErrorMessage);

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
            ["targetColumnId"] = targetColumnGuid,
            ["targetPosition"] = 0
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
            return Error(result.ErrorMessage);

        return ProposalCreated(result.Value.Id, "Proposal created. Review and approve in Taskdeck to move the card.");
    }

    /// <summary>
    /// Creates a PROPOSAL to update card fields (title, description, labels).
    /// The card is NOT updated immediately -- the proposal must be approved first.
    /// Returns the proposal ID.
    /// </summary>
    [McpServerTool(Name = "update_card"), Description(
        "Creates a PROPOSAL to update card fields (title, description, labels). " +
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
        string? label_ids = null)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(board_id, out var boardGuid))
            return Error("Invalid board_id format");
        if (!Guid.TryParse(card_id, out var cardGuid))
            return Error("Invalid card_id format");

        if (title == null && description == null && label_ids == null)
            return Error("At least one field (title, description, or label_ids) must be provided");

        var parameters = new Dictionary<string, object?>
        {
            ["boardId"] = boardGuid,
            ["cardId"] = cardGuid
        };

        if (title != null) parameters["title"] = title;
        if (description != null) parameters["description"] = description;
        if (label_ids != null) parameters["labelIds"] = ParseGuidList(label_ids);

        var summary = title != null ? $"Update card: {title}" : "Update card fields";

        var dto = new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: summary,
            RiskLevel: RiskLevel.Medium,
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
            return Error(result.ErrorMessage);

        return ProposalCreated(result.Value.Id, "Proposal created. Review and approve in Taskdeck to update the card.");
    }

    /// <summary>
    /// Creates a PROPOSAL to archive a card. The card is NOT archived immediately --
    /// the proposal must be approved. Returns the proposal ID.
    /// </summary>
    [McpServerTool(Name = "archive_card"), Description(
        "Creates a PROPOSAL to archive a card. The card is NOT archived immediately -- " +
        "the proposal must be approved. Returns the proposal ID.")]
    public async Task<string> ArchiveCard(
        [Description("Board ID (UUID)")]
        string board_id,
        [Description("Card ID to archive (UUID)")]
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
            return Error(result.ErrorMessage);

        return ProposalCreated(result.Value.Id, "Proposal created. Review and approve in Taskdeck to archive the card.");
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
            return Error(result.ErrorMessage);

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

        var parameters = new Dictionary<string, object?>
        {
            ["boardId"] = boardGuid,
            ["name"] = name
        };

        if (wip_limit.HasValue)
            parameters["wipLimit"] = wip_limit.Value;

        var dto = new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: $"Create column: {name}",
            RiskLevel: RiskLevel.Medium,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardGuid,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 0,
                    ActionType: "create",
                    TargetType: "column",
                    Parameters: JsonSerializer.Serialize(parameters, BoardResources.SerializerOptions),
                    IdempotencyKey: Guid.NewGuid().ToString())
            });

        var result = await _proposalService.CreateProposalAsync(dto);
        if (!result.IsSuccess)
            return Error(result.ErrorMessage);

        return ProposalCreated(result.Value.Id, "Proposal created. Review and approve in Taskdeck to create the column.");
    }

    private static List<Guid> ParseGuidList(string commaSeparated)
    {
        return commaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();
    }

    private static string Error(string message)
    {
        return JsonSerializer.Serialize(new { error = message }, BoardResources.SerializerOptions);
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
