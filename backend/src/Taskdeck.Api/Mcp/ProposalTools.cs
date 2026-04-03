using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Api.Mcp;

/// <summary>
/// MCP proposal management tools. Read-only + dismiss operations.
/// approve_proposal is intentionally excluded (GP-06: agents must not approve
/// their own proposals).
/// </summary>
[McpServerToolType]
public class ProposalTools
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IUserContextProvider _userContext;

    public ProposalTools(
        IAutomationProposalService proposalService,
        IUserContextProvider userContext)
    {
        _proposalService = proposalService;
        _userContext = userContext;
    }

    /// <summary>
    /// Check the current status of an automation proposal. Returns the proposal
    /// status (Pending, Approved, Applied, Rejected, Failed, Expired) and its
    /// operations.
    /// </summary>
    [McpServerTool(Name = "get_proposal_status"), Description(
        "Check the current status of an automation proposal. Returns the proposal status " +
        "(Pending, Approved, Applied, Rejected, Failed, Expired) and its operations.")]
    public async Task<string> GetProposalStatus(
        [Description("Proposal ID (UUID)")]
        string proposal_id)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(proposal_id, out var proposalGuid))
            return Error("Invalid proposal_id format");

        var result = await _proposalService.GetProposalByIdAsync(proposalGuid);
        if (!result.IsSuccess)
            return Error(result.ErrorMessage);

        var p = result.Value;

        // Verify the proposal belongs to the current user
        if (p.RequestedByUserId != userId)
            return Error("Proposal not found or access denied");

        return JsonSerializer.Serialize(new
        {
            id = p.Id,
            summary = p.Summary,
            status = p.Status.ToString(),
            riskLevel = p.RiskLevel.ToString(),
            operationCount = p.Operations.Count,
            boardId = p.BoardId,
            createdAt = p.CreatedAt,
            updatedAt = p.UpdatedAt,
            expiresAt = p.ExpiresAt,
            isExpired = p.IsExpired,
            failureReason = p.FailureReason
        }, BoardResources.SerializerOptions);
    }

    /// <summary>
    /// List automation proposals. Defaults to pending proposals. Useful for checking
    /// what proposals are awaiting review.
    /// </summary>
    [McpServerTool(Name = "list_proposals"), Description(
        "List automation proposals. Defaults to pending proposals. Useful for checking " +
        "what proposals are awaiting review.")]
    public async Task<string> ListProposals(
        [Description("Optional. Filter by status: PendingReview, Approved, Applied, Rejected, Failed, Expired, Dismissed. Default: PendingReview.")]
        string? status = null,
        [Description("Optional. Filter by board (UUID).")]
        string? board_id = null)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        ProposalStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            // Map user-friendly names to enum
            statusFilter = status.ToLowerInvariant() switch
            {
                "pending" or "pendingreview" => ProposalStatus.PendingReview,
                "approved" => ProposalStatus.Approved,
                "applied" => ProposalStatus.Applied,
                "rejected" => ProposalStatus.Rejected,
                "failed" => ProposalStatus.Failed,
                "expired" => ProposalStatus.Expired,
                "dismissed" => ProposalStatus.Dismissed,
                _ => null
            };

            if (statusFilter == null)
                return Error($"Invalid status '{status}'. Valid values: PendingReview, Approved, Applied, Rejected, Failed, Expired, Dismissed.");
        }

        Guid? boardGuid = null;
        if (!string.IsNullOrWhiteSpace(board_id))
        {
            if (!Guid.TryParse(board_id, out var parsed))
                return Error("Invalid board_id format");
            boardGuid = parsed;
        }

        var filter = new ProposalFilterDto(
            Status: statusFilter,
            BoardId: boardGuid,
            UserId: userId);

        var result = await _proposalService.GetProposalsAsync(filter);
        if (!result.IsSuccess)
            return Error(result.ErrorMessage);

        var proposals = result.Value.Select(p => new
        {
            id = p.Id,
            summary = p.Summary,
            status = p.Status.ToString(),
            riskLevel = p.RiskLevel.ToString(),
            operationCount = p.Operations.Count,
            boardId = p.BoardId,
            createdAt = p.CreatedAt
        });

        return JsonSerializer.Serialize(new
        {
            proposals,
            totalCount = result.Value.Count()
        }, BoardResources.SerializerOptions);
    }

    /// <summary>
    /// Dismiss a completed proposal (Applied, Rejected, Failed, or Expired) so it no longer
    /// appears in the default review list. Cannot dismiss pending proposals.
    /// </summary>
    [McpServerTool(Name = "dismiss_proposal"), Description(
        "Dismiss a completed proposal (Applied, Rejected, Failed, or Expired) so it no longer " +
        "appears in the default review list. Cannot dismiss pending proposals.")]
    public async Task<string> DismissProposal(
        [Description("Proposal ID to dismiss (UUID)")]
        string proposal_id)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(proposal_id, out var proposalGuid))
            return Error("Invalid proposal_id format");

        // Verify the proposal belongs to the current user before dismissing
        var getResult = await _proposalService.GetProposalByIdAsync(proposalGuid);
        if (!getResult.IsSuccess)
            return Error(getResult.ErrorMessage);

        if (getResult.Value.RequestedByUserId != userId)
            return Error("Proposal not found or access denied");

        var result = await _proposalService.DismissProposalsAsync(new List<Guid> { proposalGuid });
        if (!result.IsSuccess)
            return Error(result.ErrorMessage);

        return JsonSerializer.Serialize(new
        {
            dismissed = result.Value,
            message = result.Value > 0
                ? "Proposal dismissed successfully."
                : "Proposal could not be dismissed (it may be pending or already dismissed)."
        }, BoardResources.SerializerOptions);
    }

    private static string Error(string message)
    {
        return JsonSerializer.Serialize(new { error = message }, BoardResources.SerializerOptions);
    }
}
