using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the propose_create_column tool: creates a proposal to add a new column to the board.
/// Always produces a proposal (GP-06 compliance — never direct mutation).
/// </summary>
public sealed class ProposeCreateColumnExecutor : IToolExecutor
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly IUnitOfWork _unitOfWork;

    public ProposeCreateColumnExecutor(
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        IUnitOfWork unitOfWork)
    {
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _unitOfWork = unitOfWork;
    }

    public string ToolName => "propose_create_column";

    public Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            error = "propose_create_column requires user context",
            suggestion = "This is an internal error; please try again"
        }, ToolJsonOptions.Default));
    }

    public async Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var name = arguments.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(name))
        {
            return JsonSerializer.Serialize(new
            {
                error = "name is required",
                suggestion = "Provide a name for the new column"
            }, ToolJsonOptions.Default);
        }

        int? position = null;
        if (arguments.TryGetProperty("position", out var p) && p.ValueKind == JsonValueKind.Number)
        {
            var rawPosition = p.GetInt32();
            if (rawPosition < 0)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "position must be non-negative",
                    suggestion = "Use 0 for the first position or omit to append at end"
                }, ToolJsonOptions.Default);
            }
            position = rawPosition;
        }

        // Check for duplicate column name
        var columns = await _unitOfWork.Columns.GetByBoardIdAsync(context.BoardId, ct);
        var existing = columns.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Column '{name}' already exists",
                suggestion = "Choose a different column name"
            }, ToolJsonOptions.Default);
        }

        // If no position specified, append at end
        var resolvedPosition = position ?? columns.Count();

        var parameters = JsonSerializer.Serialize(new
        {
            boardId = context.BoardId,
            name,
            position = resolvedPosition
        });

        var operations = new List<CreateProposalOperationDto>
        {
            new(0, "create", "column", parameters, Guid.NewGuid().ToString())
        };

        var operationDtos = operations.Select(o => new ProposalOperationDto(
            Guid.Empty, Guid.Empty, o.Sequence, o.ActionType,
            o.TargetType, o.TargetId, o.Parameters, o.IdempotencyKey, o.ExpectedVersion
        )).ToList();

        var riskLevel = _policyEngine.ClassifyRisk(operationDtos);

        var summary = $"Create column '{name}' at position {resolvedPosition}";
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
}
