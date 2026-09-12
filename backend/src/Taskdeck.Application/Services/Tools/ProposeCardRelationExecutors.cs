using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Shared chat producer for one typed card relation operation. It validates
/// against the trusted chat context and creates a proposal only; Apply remains
/// the sole mutation path.
/// </summary>
public abstract class ProposeCardRelationExecutor : IToolExecutor
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IBoardRelationService _relations;
    private readonly bool _remove;

    protected ProposeCardRelationExecutor(
        IAutomationProposalService proposalService,
        IBoardRelationService relations,
        bool remove)
    {
        _proposalService = proposalService;
        _relations = relations;
        _remove = remove;
    }

    public string ToolName => _remove ? "propose_remove_card_relation" : "propose_add_card_relation";

    public Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default) =>
        Task.FromResult(JsonSerializer.Serialize(new
        {
            error = $"{ToolName} requires user context",
            suggestion = "This is an internal error; please try again"
        }, ToolJsonOptions.Default));

    public async Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !TryGetGuid(arguments, "card_id", out var cardId) ||
            !TryGetGuid(arguments, "related_card_id", out var relatedCardId))
        {
            return Error("card_id and related_card_id must be UUIDs on the current board.");
        }

        if (!arguments.TryGetProperty("relation_type", out var relationTypeElement) ||
            relationTypeElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(relationTypeElement.GetString()))
        {
            return Error("relation_type is required.");
        }

        if (!arguments.TryGetProperty("expected_revision", out var revisionElement) ||
            revisionElement.ValueKind != JsonValueKind.Number ||
            !revisionElement.TryGetInt64(out var expectedRevision) || expectedRevision < 0)
        {
            return Error("expected_revision must be a non-negative integer from get_board_card_relations.");
        }

        var relationType = relationTypeElement.GetString()!;
        var edge = new CardRelationEdge(cardId, relatedCardId, relationType);
        // The context identity and board are server-generated. The shared service checks
        // that actor, both active endpoints, board scope and the exact supplied revision.
        var validation = await _relations.ValidateMutationAsync(
            context.UserId, context.BoardId, edge, expectedRevision, _remove, ct);
        if (!validation.IsSuccess)
            return Error(validation.ErrorMessage);

        var action = _remove ? "remove-relation" : "add-relation";
        var parameters = JsonSerializer.Serialize(new
        {
            boardId = context.BoardId,
            cardId,
            relatedCardId,
            relationType,
            expectedRevision
        }, ToolJsonOptions.Default);
        var summary = _remove ? "Remove card relation" : "Add card relation";
        var proposal = new CreateProposalDto(
            ProposalSourceType.Chat,
            context.UserId,
            summary,
            RiskLevel.Medium,
            Guid.NewGuid().ToString(),
            context.BoardId,
            Operations: [new(0, action, "card", parameters, Guid.NewGuid().ToString(), cardId.ToString())])
        {
            ProvenanceProvider = context.ProducerMetadata?.Provider,
            ProvenanceModelId = context.ProducerMetadata?.Model,
            ProvenancePromptVersion = context.ProducerMetadata?.PromptVersion
        };
        var result = await _proposalService.CreateProposalAsync(proposal, ct);
        if (!result.IsSuccess)
            return Error($"Failed to create proposal: {result.ErrorMessage}");

        return JsonSerializer.Serialize(new
        {
            proposal_id = BoardContextBuilder.FormatShortId(result.Value.Id),
            full_proposal_id = result.Value.Id,
            summary,
            risk = RiskLevel.Medium.ToString()
        }, ToolJsonOptions.Default);
    }

    private static bool TryGetGuid(JsonElement arguments, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return arguments.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value);
    }

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message }, ToolJsonOptions.Default);
}

public sealed class ProposeAddCardRelationExecutor(
    IAutomationProposalService proposalService,
    IBoardRelationService relations)
    : ProposeCardRelationExecutor(proposalService, relations, remove: false);

public sealed class ProposeRemoveCardRelationExecutor(
    IAutomationProposalService proposalService,
    IBoardRelationService relations)
    : ProposeCardRelationExecutor(proposalService, relations, remove: true);
