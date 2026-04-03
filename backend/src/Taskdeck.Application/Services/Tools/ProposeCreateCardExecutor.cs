using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the propose_create_card tool: creates a proposal to add a new card.
/// Always produces a proposal (GP-06 compliance — never direct mutation).
/// </summary>
public sealed class ProposeCreateCardExecutor : IToolExecutor
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly IUnitOfWork _unitOfWork;

    public ProposeCreateCardExecutor(
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        IUnitOfWork unitOfWork)
    {
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _unitOfWork = unitOfWork;
    }

    public string ToolName => "propose_create_card";

    public Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        // Write tools require userId; delegate to context-aware overload
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            error = "propose_create_card requires user context",
            suggestion = "This is an internal error; please try again"
        }, ToolJsonOptions.Default));
    }

    public async Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var title = arguments.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(title))
        {
            return JsonSerializer.Serialize(new
            {
                error = "title is required",
                suggestion = "Provide a title for the new card"
            }, ToolJsonOptions.Default);
        }

        var columnName = arguments.TryGetProperty("column_name", out var cn) ? cn.GetString() : null;
        var description = arguments.TryGetProperty("description", out var d) ? d.GetString() : null;
        var labels = ExtractStringArray(arguments, "labels");

        // Resolve column
        var columns = await _unitOfWork.Columns.GetByBoardIdAsync(context.BoardId, ct);
        Guid? columnId;
        string resolvedColumnName;

        if (!string.IsNullOrWhiteSpace(columnName))
        {
            var column = columns.FirstOrDefault(c =>
                string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
            if (column == null)
            {
                var availableNames = columns.Select(c => c.Name).ToArray();
                return JsonSerializer.Serialize(new
                {
                    error = $"Column '{columnName}' not found",
                    suggestion = "Use list_board_columns to see available columns",
                    available_columns = availableNames
                }, ToolJsonOptions.Default);
            }
            columnId = column.Id;
            resolvedColumnName = column.Name;
        }
        else
        {
            var firstColumn = columns.OrderBy(c => c.Position).FirstOrDefault();
            if (firstColumn == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "No columns found in board",
                    suggestion = "Use propose_create_column to create a column first"
                }, ToolJsonOptions.Default);
            }
            columnId = firstColumn.Id;
            resolvedColumnName = firstColumn.Name;
        }

        // Build proposal operations
        var parameters = JsonSerializer.Serialize(new
        {
            title,
            description,
            columnId,
            boardId = context.BoardId,
            labels
        });

        var operations = new List<CreateProposalOperationDto>
        {
            new(0, "create", "card", parameters, Guid.NewGuid().ToString())
        };

        var operationDtos = operations.Select(o => new ProposalOperationDto(
            Guid.Empty, Guid.Empty, o.Sequence, o.ActionType,
            o.TargetType, o.TargetId, o.Parameters, o.IdempotencyKey, o.ExpectedVersion
        )).ToList();

        var riskLevel = _policyEngine.ClassifyRisk(operationDtos);

        var summary = $"Create card '{title}' in {resolvedColumnName}";
        if (summary.Length > 500) summary = summary[..497] + "...";

        var createDto = new CreateProposalDto(
            ProposalSourceType.Chat,
            context.UserId,
            summary,
            riskLevel,
            Guid.NewGuid().ToString(),
            context.BoardId,
            null,
            1440,
            operations
        );

        var result = await _proposalService.CreateProposalAsync(createDto, ct);
        if (!result.IsSuccess)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to create proposal: {result.ErrorMessage}"
            }, ToolJsonOptions.Default);
        }

        return JsonSerializer.Serialize(new
        {
            proposal_id = BoardContextBuilder.FormatShortId(result.Value.Id),
            full_proposal_id = result.Value.Id,
            summary,
            risk = riskLevel.ToString()
        }, ToolJsonOptions.Default);
    }

    private static string[] ExtractStringArray(JsonElement args, string propertyName)
    {
        if (!args.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }
}
