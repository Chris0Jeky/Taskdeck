using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// API endpoints for managing automation proposals and their lifecycle.
/// </summary>
[ApiController]
[Authorize]
[Route("api/automation/proposals")]
public class AutomationProposalsController : AuthenticatedControllerBase
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationExecutorService _executorService;

    public AutomationProposalsController(
        IAutomationProposalService proposalService,
        IAutomationExecutorService executorService,
        IUserContext userContext) : base(userContext)
    {
        _proposalService = proposalService;
        _executorService = executorService;
    }

    /// <summary>
    /// Gets a list of automation proposals with optional filters.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProposals(
        [FromQuery] ProposalStatus? status,
        [FromQuery] Guid? boardId,
        [FromQuery] Guid? userId,
        [FromQuery] RiskLevel? riskLevel,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var filter = new ProposalFilterDto(status, boardId, userId, riskLevel, limit);
        var result = await _proposalService.GetProposalsAsync(filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Gets a specific automation proposal by ID with all operations.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProposal(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _proposalService.GetProposalByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Creates a new automation proposal with operations.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateProposal([FromBody] CreateProposalDto dto, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var requestedByUserId, out var errorResult))
            return errorResult!;

        var createDto = dto with
        {
            RequestedByUserId = requestedByUserId
        };

        var result = await _proposalService.CreateProposalAsync(createDto, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetProposal), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    /// <summary>
    /// Approves a pending automation proposal.
    /// </summary>
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApproveProposal(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var decidedByUserId, out var errorResult))
            return errorResult!;

        var result = await _proposalService.ApproveProposalAsync(id, decidedByUserId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Rejects a pending automation proposal.
    /// </summary>
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectProposal(
        Guid id,
        [FromBody] UpdateProposalStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var decidedByUserId, out var errorResult))
            return errorResult!;

        var result = await _proposalService.RejectProposalAsync(id, decidedByUserId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Executes an approved automation proposal through the automation executor.
    /// </summary>
    [HttpPost("{id}/execute")]
    public async Task<IActionResult> ExecuteProposal(Guid id, CancellationToken cancellationToken = default)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ||
            string.IsNullOrWhiteSpace(idempotencyHeader))
        {
            return BadRequest(new
            {
                errorCode = ErrorCodes.ValidationError,
                message = "Idempotency-Key header is required"
            });
        }

        var executionResult = await _executorService.ExecuteProposalAsync(id, idempotencyHeader.ToString(), cancellationToken);
        if (!executionResult.IsSuccess)
            return executionResult.ToErrorActionResult();

        var result = await _proposalService.GetProposalByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Gets a diff preview for a proposal showing what changes will be made.
    /// </summary>
    [HttpGet("{id}/diff")]
    public async Task<IActionResult> GetProposalDiff(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _proposalService.GetProposalDiffAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(new { diff = result.Value }) : result.ToErrorActionResult();
    }
}
