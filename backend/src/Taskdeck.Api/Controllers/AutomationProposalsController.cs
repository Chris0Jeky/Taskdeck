using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// API endpoints for managing automation proposals and their lifecycle.
/// </summary>
[ApiController]
[Route("api/automation/proposals")]
public class AutomationProposalsController : ControllerBase
{
    private readonly IAutomationProposalService _proposalService;

    public AutomationProposalsController(IAutomationProposalService proposalService)
    {
        _proposalService = proposalService;
    }

    /// <summary>
    /// Gets a list of automation proposals with optional filters.
    /// </summary>
    /// <param name="status">Filter by proposal status</param>
    /// <param name="boardId">Filter by board ID</param>
    /// <param name="userId">Filter by user ID</param>
    /// <param name="riskLevel">Filter by risk level</param>
    /// <param name="limit">Maximum number of results (default: 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of proposals</returns>
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

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.ErrorCode switch
        {
            "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
            "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
            _ => Problem(result.ErrorMessage, statusCode: 500)
        };
    }

    /// <summary>
    /// Gets a specific automation proposal by ID with all operations.
    /// </summary>
    /// <param name="id">Proposal ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Proposal details</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProposal(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _proposalService.GetProposalByIdAsync(id, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Creates a new automation proposal with operations.
    /// </summary>
    /// <param name="dto">Proposal creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created proposal</returns>
    [HttpPost]
    public async Task<IActionResult> CreateProposal([FromBody] CreateProposalDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _proposalService.CreateProposalAsync(dto, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return CreatedAtAction(nameof(GetProposal), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>
    /// Approves a pending automation proposal.
    /// </summary>
    /// <param name="id">Proposal ID</param>
    /// <param name="decidedByUserId">User ID approving the proposal</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated proposal</returns>
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApproveProposal(
        Guid id,
        [FromQuery] Guid decidedByUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await _proposalService.ApproveProposalAsync(id, decidedByUserId, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Rejects a pending automation proposal.
    /// </summary>
    /// <param name="id">Proposal ID</param>
    /// <param name="decidedByUserId">User ID rejecting the proposal</param>
    /// <param name="dto">Rejection details (reason required for High/Critical risk)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated proposal</returns>
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectProposal(
        Guid id,
        [FromQuery] Guid decidedByUserId,
        [FromBody] UpdateProposalStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _proposalService.RejectProposalAsync(id, decidedByUserId, dto, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Executes an approved automation proposal by marking it as applied.
    /// </summary>
    /// <param name="id">Proposal ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated proposal</returns>
    [HttpPost("{id}/execute")]
    public async Task<IActionResult> ExecuteProposal(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _proposalService.MarkAsAppliedAsync(id, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Gets a diff preview for a proposal showing what changes will be made.
    /// </summary>
    /// <param name="id">Proposal ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Diff preview text</returns>
    [HttpGet("{id}/diff")]
    public async Task<IActionResult> GetProposalDiff(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _proposalService.GetProposalDiffAsync(id, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return Ok(new { diff = result.Value });
    }
}
