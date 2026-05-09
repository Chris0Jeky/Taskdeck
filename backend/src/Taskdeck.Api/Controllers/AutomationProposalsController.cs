using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Confidence;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// API endpoints for managing automation proposals and their lifecycle.
/// </summary>
[ApiController]
[Authorize]
[Route("api/automation/proposals")]
public class AutomationProposalsController : AuthenticatedControllerBase
{
    private const int DefaultProposalListLimit = 100;
    private const int MaxProposalListLimit = 500;
    private const int UnscopedProposalOverfetchMultiplier = 4;

    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationExecutorService _executorService;
    private readonly ISimilarDecisionService _similarDecisionService;
    private readonly BoardAuthorizationService _authorizationService;
    private readonly IProposalConflictDetector _conflictDetector;
    private readonly IProvenanceQueryService _provenanceQueryService;
    private readonly IConfidenceBreakdownService _confidenceBreakdownService;
    private readonly ICardHistoryService _cardHistoryService;
    private readonly ISideEffectAnalyzer _sideEffectAnalyzer;
    private readonly IProposalRevisionService _revisionService;

    public AutomationProposalsController(
        IAutomationProposalService proposalService,
        IAutomationExecutorService executorService,
        ISimilarDecisionService similarDecisionService,
        BoardAuthorizationService authorizationService,
        IProposalConflictDetector conflictDetector,
        IProvenanceQueryService provenanceQueryService,
        IConfidenceBreakdownService confidenceBreakdownService,
        ICardHistoryService cardHistoryService,
        ISideEffectAnalyzer sideEffectAnalyzer,
        IProposalRevisionService revisionService,
        IUserContext userContext) : base(userContext)
    {
        _proposalService = proposalService;
        _executorService = executorService;
        _similarDecisionService = similarDecisionService;
        _authorizationService = authorizationService;
        _conflictDetector = conflictDetector;
        _provenanceQueryService = provenanceQueryService;
        _confidenceBreakdownService = confidenceBreakdownService;
        _cardHistoryService = cardHistoryService;
        _sideEffectAnalyzer = sideEffectAnalyzer;
        _revisionService = revisionService;
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
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        if (userId.HasValue && userId.Value != callerUserId)
        {
            return Result.Failure(ErrorCodes.Forbidden, "You can only query proposals for your own user.").ToErrorActionResult();
        }

        if (boardId.HasValue)
        {
            var permissionError = await EnsureBoardPermissionAsync(
                _authorizationService,
                callerUserId,
                boardId.Value,
                static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
                "You do not have permission to view this board");

            if (permissionError is not null)
                return permissionError;
        }

        var requestLimit = NormalizeRequestLimit(limit);
        var effectiveUserId = userId ?? (boardId.HasValue ? null : callerUserId);
        var queryLimit = boardId.HasValue
            ? requestLimit
            : Math.Clamp(requestLimit * UnscopedProposalOverfetchMultiplier, requestLimit, MaxProposalListLimit);
        var filter = new ProposalFilterDto(status, boardId, effectiveUserId, riskLevel, queryLimit);
        var result = await _proposalService.GetProposalsAsync(filter, cancellationToken);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var proposals = result.Value.ToList();
        if (!boardId.HasValue)
        {
            var boardScopedIds = proposals
                .Where(p => p.BoardId.HasValue)
                .Select(p => p.BoardId!.Value)
                .Distinct()
                .ToArray();

            if (boardScopedIds.Length > 0)
            {
                var readableBoardIdsResult = await _authorizationService.GetReadableBoardIdsAsync(
                    callerUserId,
                    boardScopedIds,
                    cancellationToken);

                if (!readableBoardIdsResult.IsSuccess)
                    return readableBoardIdsResult.ToErrorActionResult();

                var readableBoardIds = readableBoardIdsResult.Value;
                proposals = proposals
                    .Where(p => !p.BoardId.HasValue || readableBoardIds.Contains(p.BoardId.Value))
                    .ToList();
            }
        }

        return Ok(proposals.Take(requestLimit));
    }

    /// <summary>
    /// Gets a specific automation proposal by ID with all operations.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProposal(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        return Ok(auth.Proposal);
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

        var auth = await AuthorizeProposalAsync(id, decidedByUserId, requireWriteAccess: true, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

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

        var auth = await AuthorizeProposalAsync(id, decidedByUserId, requireWriteAccess: true, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _proposalService.RejectProposalAsync(id, decidedByUserId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Executes an approved automation proposal through the automation executor.
    /// </summary>
    [HttpPost("{id}/execute")]
    public async Task<IActionResult> ExecuteProposal(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: true, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        if (!Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ||
            string.IsNullOrWhiteSpace(idempotencyHeader))
        {
            return BadRequest(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                "Idempotency-Key header is required"));
        }

        var executionResult = await _executorService.ExecuteProposalAsync(id, idempotencyHeader.ToString(), cancellationToken);
        if (!executionResult.IsSuccess)
            return executionResult.ToErrorActionResult();

        var result = await _proposalService.GetProposalByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Dismisses completed proposals so they no longer appear in the default review list.
    /// Accepts an array of proposal IDs; only proposals in dismissable states
    /// (Applied, Rejected, Failed, Expired, or Approved-but-expired) will be dismissed.
    /// </summary>
    [HttpPost("dismiss")]
    public async Task<IActionResult> DismissProposals(
        [FromBody] DismissProposalsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        if (request.Ids is null || request.Ids.Count == 0)
        {
            return BadRequest(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                "At least one proposal ID is required"));
        }

        if (request.Ids.Count > MaxProposalListLimit)
        {
            return BadRequest(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                $"Cannot dismiss more than {MaxProposalListLimit} proposals at once"));
        }

        // Verify the caller owns each proposal being dismissed
        foreach (var proposalId in request.Ids.Distinct())
        {
            var proposalResult = await _proposalService.GetProposalByIdAsync(proposalId, cancellationToken);
            if (!proposalResult.IsSuccess)
                return proposalResult.ToErrorActionResult();

            if (proposalResult.Value.RequestedByUserId != callerUserId)
            {
                return Result.Failure(ErrorCodes.Forbidden, "You can only dismiss your own proposals.").ToErrorActionResult();
            }
        }

        var result = await _proposalService.DismissProposalsAsync(request.Ids, cancellationToken);
        return result.IsSuccess
            ? Ok(new { dismissed = result.Value })
            : result.ToErrorActionResult();
    }

    /// <summary>
    /// Gets tone-classified conflict/warning/status rows for a proposal.
    /// </summary>
    [HttpGet("{id}/conflicts")]
    public async Task<IActionResult> GetProposalConflicts(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _conflictDetector.DetectConflictsAsync(id, callerUserId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Gets the card history ledger for a proposal, showing all touches on affected cards.
    /// </summary>
    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetProposalHistory(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _cardHistoryService.GetCardHistoryForProposalAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Gets the side-effect analysis for a proposal, including the 7-category breakdown
    /// and reversibility posture.
    /// </summary>
    [HttpGet("{id}/side-effects")]
    public async Task<IActionResult> GetProposalSideEffects(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _sideEffectAnalyzer.AnalyzeAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    /// <summary>
    /// Gets a diff preview for a proposal showing what changes will be made.
    /// </summary>
    [HttpGet("{id}/diff")]
    public async Task<IActionResult> GetProposalDiff(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _proposalService.GetProposalDiffAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(new { diff = result.Value }) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Gets the provenance rows for a proposal, describing the sources read,
    /// excluded, or inferred during proposal generation.
    /// </summary>
    [HttpGet("{id}/provenance")]
    public async Task<IActionResult> GetProposalProvenance(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _provenanceQueryService.GetProvenanceRowsAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Gets the multi-component confidence breakdown for a proposal.
    /// </summary>
    [HttpGet("{id}/confidence")]
    public async Task<IActionResult> GetProposalConfidence(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _confidenceBreakdownService.GetBreakdownAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Gets similar past decisions for a proposal, including the latest 3 decisions
    /// with the same action class and an aggregate apply rate.
    /// </summary>
    [HttpGet("{id}/similar-past")]
    public async Task<IActionResult> GetSimilarPast(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(id, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _similarDecisionService.GetSimilarPastAsync(id, callerUserId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("{proposalId}/revisions")]
    public async Task<IActionResult> CreateRevision(
        Guid proposalId,
        [FromBody] CreateRevisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(proposalId, callerUserId, requireWriteAccess: true, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var dto = new CreateProposalRevisionDto(
            proposalId,
            callerUserId,
            request.RevisedPayload,
            request.Reason);

        var result = await _revisionService.CreateRevisionAsync(dto, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetLatestRevision), new { proposalId }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpGet("{proposalId}/revisions")]
    public async Task<IActionResult> GetRevisions(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(proposalId, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _revisionService.GetRevisionsForProposalAsync(proposalId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("{proposalId}/revisions/latest")]
    public async Task<IActionResult> GetLatestRevision(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var auth = await AuthorizeProposalAsync(proposalId, callerUserId, requireWriteAccess: false, cancellationToken);
        if (auth.ErrorResult is not null)
            return auth.ErrorResult;

        var result = await _revisionService.GetLatestRevisionAsync(proposalId, cancellationToken);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        return result.Value is not null
            ? Ok(result.Value)
            : NotFound(new ApiErrorResponse(ErrorCodes.NotFound, "No revisions exist for this proposal"));
    }

    private async Task<(ProposalDto? Proposal, IActionResult? ErrorResult)> AuthorizeProposalAsync(
        Guid proposalId,
        Guid callerUserId,
        bool requireWriteAccess,
        CancellationToken cancellationToken)
    {
        var proposalResult = await _proposalService.GetProposalByIdAsync(proposalId, cancellationToken);
        if (!proposalResult.IsSuccess)
            return (null, proposalResult.ToErrorActionResult());

        var proposal = proposalResult.Value;

        if (proposal.BoardId.HasValue)
        {
            var permissionError = await EnsureBoardPermissionAsync(
                _authorizationService,
                callerUserId,
                proposal.BoardId.Value,
                requireWriteAccess
                    ? static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId)
                    : static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
                requireWriteAccess
                    ? "You do not have permission to modify this board"
                    : "You do not have permission to view this board");

            return permissionError is null
                ? (proposal, null)
                : (null, permissionError);
        }

        if (proposal.RequestedByUserId != callerUserId)
        {
            return (null, Result.Failure(ErrorCodes.Forbidden, "You do not have permission to access this proposal.").ToErrorActionResult());
        }

        return (proposal, null);
    }

    private static int NormalizeRequestLimit(int requestedLimit)
    {
        if (requestedLimit <= 0)
            return DefaultProposalListLimit;

        return Math.Clamp(requestedLimit, 1, MaxProposalListLimit);
    }
}
