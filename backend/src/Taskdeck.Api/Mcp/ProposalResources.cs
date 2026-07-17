using System.Text.Json;
using ModelContextProtocol.Server;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Api.Mcp;

/// <summary>
/// MCP resource provider for Taskdeck automation proposals.
/// Exposes proposals as read-only MCP resources.
/// </summary>
[McpServerResourceType]
public class ProposalResources
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IUserContextProvider _userContext;

    public ProposalResources(
        IAutomationProposalService proposalService,
        IUserContextProvider userContext)
    {
        _proposalService = proposalService;
        _userContext = userContext;
    }

    /// <summary>
    /// Lists pending proposals for the current user.
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://proposals",
        Name = "proposals",
        Title = "Pending Proposals",
        MimeType = "application/json")]
    public async Task<string> ListProposals()
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        var result = await _proposalService.GetProposalsAsync(new ProposalFilterDto(UserId: userId));
        if (!result.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to list proposals: {result.ErrorMessage}");

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
    /// Returns detail for a single proposal including operations and diff.
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://proposals/{proposalId}",
        Name = "proposal_detail",
        Title = "Proposal Detail",
        MimeType = "application/json")]
    public async Task<string> GetProposalDetail(string proposalId)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(proposalId, out var proposalGuid))
            throw new ArgumentException($"MCP: invalid proposal ID '{proposalId}'");

        var result = await _proposalService.GetProposalByIdAsync(proposalGuid);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to get proposal: {result.ErrorMessage}");

        var p = result.Value;

        // Verify the proposal belongs to the current user
        if (p.RequestedByUserId != userId)
            throw new InvalidOperationException("MCP: proposal not found or access denied");

        // The stored DiffPreview must never be served raw: that is the MCP preview==apply
        // trust-violation this resource closes (#1415), the same class the HTTP diff surface
        // already closed (#1370/#1376/#1398/#1413). Route the preview through the diff-path
        // gates instead. PendingReview/Approved proposals go through GetProposalDiffAsync — the
        // single source of truth for the live, fully-gated diff (structure + expiry from
        // #1376/#1395, requester-exists / board-exists / board-access from #1398/#1413). Decided
        // (terminal) proposals serve the STORED preview (a live rebuild would describe a board
        // that has since moved — the #1397 decision) but still re-check the requester/board-access
        // gate, so a reviewer who lost board access — or whose board was deleted — is denied it.
        var isTerminal = IsTerminalStatus(p.Status);
        Result<string> previewResult = isTerminal
            ? await _proposalService.GetTerminalProposalStoredPreviewAsync(p.Id)
            : await _proposalService.GetProposalDiffAsync(p.Id);

        // Surface the service's own error message exactly as the GetProposalByIdAsync failure
        // above and the MCP write tools do (WriteTools/ProposalTools raise result.ErrorMessage) —
        // no new MCP error shape is invented for the gate denial.
        if (!previewResult.IsSuccess)
            throw new InvalidOperationException($"MCP: {previewResult.ErrorMessage}");

        var operations = p.Operations.Select(op => new
        {
            sequence = op.Sequence,
            actionType = op.ActionType,
            targetType = op.TargetType,
            targetId = op.TargetId,
            parameters = op.Parameters
        });

        return JsonSerializer.Serialize(new
        {
            id = p.Id,
            summary = p.Summary,
            status = p.Status.ToString(),
            riskLevel = p.RiskLevel.ToString(),
            boardId = p.BoardId,
            operations,
            operationCount = p.Operations.Count,
            diffPreview = previewResult.Value,
            // Explicit provenance marker: "live" = freshly gated diff for an open proposal;
            // "stored" = historical preview for a decided (terminal) proposal (#1397/#1415).
            diffPreviewSource = isTerminal ? "stored" : "live",
            createdAt = p.CreatedAt,
            updatedAt = p.UpdatedAt,
            expiresAt = p.ExpiresAt,
            isExpired = p.IsExpired,
            failureReason = p.FailureReason
        }, BoardResources.SerializerOptions);
    }

    /// <summary>
    /// True for decided (terminal) proposals — Applied, Rejected, Failed, Expired, Dismissed —
    /// whose diff is historical and served from the stored preview. PendingReview and Approved
    /// are open states whose diff is rebuilt live through the full gate chain.
    /// </summary>
    private static bool IsTerminalStatus(ProposalStatus status) =>
        status is ProposalStatus.Applied
            or ProposalStatus.Rejected
            or ProposalStatus.Failed
            or ProposalStatus.Expired
            or ProposalStatus.Dismissed;
}
