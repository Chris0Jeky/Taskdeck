using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the propose_archive_card tool: creates a proposal to archive a card.
/// Risk level: High. Always produces a proposal (GP-06 compliance — never direct mutation).
/// </summary>
public sealed class ProposeArchiveCardExecutor : IToolExecutor
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly IUnitOfWork _unitOfWork;

    public ProposeArchiveCardExecutor(
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        IUnitOfWork unitOfWork)
    {
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _unitOfWork = unitOfWork;
    }

    public string ToolName => "propose_archive_card";

    public Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            error = "propose_archive_card requires user context",
            suggestion = "This is an internal error; please try again"
        }, ToolJsonOptions.Default));
    }

    public async Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var cardIdStr = arguments.TryGetProperty("card_id", out var ci) ? ci.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(cardIdStr))
        {
            return JsonSerializer.Serialize(new
            {
                error = "card_id is required",
                suggestion = "Use search_cards or list_cards_in_column to find card IDs"
            }, ToolJsonOptions.Default);
        }

        // Resolve short ID to full card
        var allCards = await _unitOfWork.Cards.GetByBoardIdAsync(context.BoardId, ct);
        var card = allCards.FirstOrDefault(c =>
            BoardContextBuilder.FormatShortId(c.Id).Equals(cardIdStr, StringComparison.OrdinalIgnoreCase));

        if (card == null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Card {cardIdStr} not found",
                suggestion = "Use search_cards to find the card"
            }, ToolJsonOptions.Default);
        }

        var parameters = JsonSerializer.Serialize(new { cardId = card.Id });

        var operations = new List<CreateProposalOperationDto>
        {
            new(0, "archive", "card", parameters, Guid.NewGuid().ToString(), TargetId: card.Id.ToString())
        };

        var operationDtos = operations.Select(o => new ProposalOperationDto(
            Guid.Empty, Guid.Empty, o.Sequence, o.ActionType,
            o.TargetType, o.TargetId, o.Parameters, o.IdempotencyKey, o.ExpectedVersion
        )).ToList();

        var riskLevel = _policyEngine.ClassifyRisk(operationDtos);

        var summary = $"Archive card '{card.Title}'";
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
