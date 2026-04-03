using System.Text.Json;
using ModelContextProtocol.Server;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

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
            diffPreview = p.DiffPreview,
            createdAt = p.CreatedAt,
            updatedAt = p.UpdatedAt,
            expiresAt = p.ExpiresAt,
            isExpired = p.IsExpired,
            failureReason = p.FailureReason
        }, BoardResources.SerializerOptions);
    }
}
