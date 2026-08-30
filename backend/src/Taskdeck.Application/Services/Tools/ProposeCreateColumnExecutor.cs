using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
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
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return JsonSerializer.Serialize(new
            {
                error = "arguments must be a JSON object",
                suggestion = "Provide a name for the new column"
            }, ToolJsonOptions.Default);
        }

        if (!OperationParameterParser.TryGetRequiredString(arguments, "name", out var name, out var nameError))
        {
            return JsonSerializer.Serialize(new
            {
                error = !arguments.TryGetProperty("name", out _) ? "name is required" : nameError,
                suggestion = "Provide a name for the new column"
            }, ToolJsonOptions.Default);
        }

        int? position = null;
        if (arguments.TryGetProperty("position", out var positionProperty))
        {
            if (positionProperty.ValueKind != JsonValueKind.Number ||
                !positionProperty.TryGetInt32(out var rawPosition))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "position must be an integer",
                    suggestion = "Use a non-negative whole number or omit to append at end"
                }, ToolJsonOptions.Default);
            }

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

        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(context.BoardId, ct)).ToList();
        // Preserve the chat surface's existing convenience warning. Column names
        // are not a domain uniqueness invariant, so preview/apply do not repeat it.
        if (columns.Any(column => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Column '{name}' already exists",
                suggestion = "Choose a different column name"
            }, ToolJsonOptions.Default);
        }

        if (!position.HasValue)
        {
            var appendPositionResult = ProposalOperationContractValidator.ResolveAppendPosition(columns);
            if (!appendPositionResult.IsSuccess)
            {
                return JsonSerializer.Serialize(new { error = appendPositionResult.ErrorMessage }, ToolJsonOptions.Default);
            }

            position = appendPositionResult.Value;
        }

        var contract = new CreateColumnOperationParameters(context.BoardId, name, position.Value, null);
        var availabilityResult = ProposalOperationContractValidator.ValidateCreateColumnPositionAvailability(columns, contract);
        if (!availabilityResult.IsSuccess)
        {
            return JsonSerializer.Serialize(new
            {
                error = availabilityResult.ErrorMessage,
                suggestion = "Choose an unoccupied position or omit to append at end"
            }, ToolJsonOptions.Default);
        }

        var parameters = JsonSerializer.Serialize(new
        {
            boardId = context.BoardId,
            name,
            position = position.Value
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

        // The chat tool must not persist a proposal the same preview/apply contract
        // would reject. This also pins requester existence and board write access.
        var validationResult = await _policyEngine.ValidatePermissionsAsync(
            context.UserId,
            context.BoardId,
            operationDtos,
            BoardAccessBar.Write,
            ct);
        if (!validationResult.IsSuccess)
        {
            return JsonSerializer.Serialize(new
            {
                error = validationResult.ErrorMessage
            }, ToolJsonOptions.Default);
        }

        var summary = $"Create column '{name}' at position {position.Value}";
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
