using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationProposalService : IAutomationProposalService
{
    private const string CaptureTriageActionType = "create";
    private const string CaptureTriageTargetType = "card";

    private static readonly HashSet<string> KnownActionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "add",
        "apply",
        "archive",
        "assign",
        "attach",
        "block",
        "create",
        "delete",
        "move",
        "remove",
        "rename",
        "reorder",
        "restore",
        "set",
        "unarchive",
        "unblock",
        "update"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IProposalProvenanceRepository? _provenanceRepository;
    private readonly IAutomationPolicyEngine _policyEngine;

    public AutomationProposalService(
        IUnitOfWork unitOfWork,
        INotificationService? notificationService = null,
        IProposalProvenanceRepository? provenanceRepository = null,
        IAutomationPolicyEngine? policyEngine = null)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService ?? NoOpNotificationService.Instance;
        _provenanceRepository = provenanceRepository;
        // Fall back to a plain engine over the same unit of work when DI does not supply one
        // (direct construction in tests). The engine is stateless apart from _unitOfWork, so
        // the fallback runs the identical read-safe permission gates the injected one does.
        _policyEngine = policyEngine ?? new AutomationPolicyEngine(unitOfWork);
    }

    public async Task<Result<ProposalDto>> CreateProposalAsync(CreateProposalDto dto, CancellationToken cancellationToken = default)
    {
        // Defensive create-time validation (issue #1125): reject malformed operation input
        // (markup/binary actionType-targetType, non-JSON or oversized/over-nested parameters)
        // with 400 before any persistence, so junk never persists and never escapes as a 500.
        var operationValidation = ProposalOperationInputValidator.Validate(dto.Operations);
        if (!operationValidation.IsSuccess)
            return Result.Failure<ProposalDto>(operationValidation.ErrorCode, operationValidation.ErrorMessage);

        try
        {
            var proposal = new AutomationProposal(
                dto.SourceType,
                dto.RequestedByUserId,
                dto.Summary,
                dto.RiskLevel,
                dto.CorrelationId,
                dto.BoardId,
                dto.SourceReferenceId,
                dto.ExpiryMinutes);

            await _unitOfWork.AutomationProposals.AddAsync(proposal, cancellationToken);

            // Add operations if provided
            if (dto.Operations != null)
            {
                foreach (var opDto in dto.Operations)
                {
                    var operation = new AutomationProposalOperation(
                        proposal.Id,
                        opDto.Sequence,
                        opDto.ActionType,
                        opDto.TargetType,
                        opDto.Parameters,
                        opDto.IdempotencyKey,
                        opDto.TargetId,
                        opDto.ExpectedVersion);

                    proposal.AddOperation(operation);
                }
            }

            if (_provenanceRepository is not null)
            {
                var provenance = BuildCreationProvenance(proposal, dto);
                await _provenanceRepository.AddAsync(provenance, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    private static ProposalProvenance BuildCreationProvenance(AutomationProposal proposal, CreateProposalDto dto)
    {
        var provenance = new ProposalProvenance(
            proposal.Id,
            proposal.CorrelationId,
            ResolveProvenanceModelId(dto),
            Math.Max(0, dto.ProvenanceTotalTokens));

        provenance.AddField(new ProvenanceField(
            "Summary",
            ProvenanceKind.Inferred,
            0.8,
            provenance.Id));

        var orderedOperations = proposal.Operations
            .OrderBy(operation => operation.Sequence)
            .ToList();

        for (var i = 0; i < orderedOperations.Count; i++)
        {
            var operation = orderedOperations[i];
            provenance.AddField(new ProvenanceField(
                TruncateProvenanceFieldName($"Operation {i + 1}: {operation.ActionType} {operation.TargetType}"),
                ProvenanceKind.Inferred,
                0.75,
                provenance.Id));
        }

        return provenance;
    }

    private static string ResolveProvenanceModelId(CreateProposalDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ProvenanceModelId))
            return TruncateProvenanceModelId(dto.ProvenanceModelId.Trim());

        return dto.SourceType switch
        {
            ProposalSourceType.Chat => "chat-tools",
            ProposalSourceType.Manual => "manual",
            ProposalSourceType.Queue => "queue",
            _ => "unknown"
        };
    }

    private static string TruncateProvenanceFieldName(string fieldName)
    {
        return fieldName.Length <= 100 ? fieldName : fieldName[..100];
    }

    private static string TruncateProvenanceModelId(string modelId)
    {
        return modelId.Length <= 100 ? modelId : modelId[..100];
    }

    public async Task<Result<ProposalDto>> GetProposalByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
        if (proposal == null)
            return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

        return Result.Success(MapToDto(proposal));
    }

    public async Task<Result<IEnumerable<ProposalDto>>> GetProposalsAsync(ProposalFilterDto? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new ProposalFilterDto();
        var limit = filter.Limit <= 0 ? 100 : filter.Limit;

        IEnumerable<AutomationProposal> proposals;

        // Apply filters in order of specificity
        if (filter.UserId.HasValue)
        {
            // Review-queue read: keep the default includeDeferred:false so snoozed pending proposals stay hidden.
            proposals = await _unitOfWork.AutomationProposals.GetByUserIdAsync(filter.UserId.Value, limit, cancellationToken: cancellationToken);
        }
        else if (filter.BoardId.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByBoardIdAsync(filter.BoardId.Value, limit, cancellationToken);
        }
        else if (filter.Status.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByStatusAsync(filter.Status.Value, limit, cancellationToken);
        }
        else if (filter.RiskLevel.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByRiskLevelAsync(filter.RiskLevel.Value, limit, cancellationToken);
        }
        else
        {
            // Get all by status Pending if no filters provided
            proposals = await _unitOfWork.AutomationProposals.GetByStatusAsync(ProposalStatus.PendingReview, limit, cancellationToken);
        }

        // Apply remaining filters in-memory when multiple filters are specified.
        if (filter.Status.HasValue)
            proposals = proposals.Where(p => p.Status == filter.Status.Value);

        if (filter.BoardId.HasValue)
            proposals = proposals.Where(p => p.BoardId == filter.BoardId.Value);

        if (filter.UserId.HasValue)
            proposals = proposals.Where(p => p.RequestedByUserId == filter.UserId.Value);

        if (filter.RiskLevel.HasValue)
            proposals = proposals.Where(p => p.RiskLevel == filter.RiskLevel.Value);

        proposals = proposals.Take(limit);

        return Result.Success(proposals.Select(MapToDto));
    }

    public async Task<Result<ProposalDto>> ApproveProposalAsync(Guid id, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            // Approve-time gates (#1416 approve == apply): a reviewer must not be able to commit
            // to a proposal the executor will refuse. Apply validates the EFFECTIVE operation set
            // (latest saved revision, else the original operations) through
            // AutomationPolicyEngine.ValidatePolicy (structure, then expiry) followed by
            // ValidatePermissionsAsync (requester exists → 404, board exists → 404, board access
            // → 403, then operation-contract validation → 400/403/404), and GetProposalDiffAsync
            // mirrors the same revision-aware materialization and gate order. Approve now enforces:
            //   1. Structure → 400 ValidationError (same validator, same shape as diff/apply).
            //   2. Expiry → 409 InvalidOperation, via the domain transition's own guard
            //      (AutomationProposal.Approve throws "Cannot approve expired proposal").
            //      Deliberately NOT the diff path's 400 read-parity shape: approving is a state
            //      transition, so a 409 conflict is the correct refusal for an expired proposal.
            //   3. Permissions + operation contract → the same 400/403/404 results Apply produces,
            //      via the same _policyEngine.ValidatePermissionsAsync call the diff path runs.
            // Ordering mirrors the diff/apply sequence exactly (structure → expiry → permissions):
            // the permission gate is skipped for an expired proposal so the domain guard's 409 owns
            // expiry — an expired proposal with revoked access reports expiry, never Forbidden,
            // matching the #1413 LOW-4 ordering pin on the diff path. A zero-op AND expired
            // proposal likewise reports the 400 structure error first, as it does on diff and apply.
            // This closes the last "user commits to something the executor will refuse" step in
            // this trust class (siblings #1370 → #1374, #1376 → #1395, #1398 → #1413).
            //
            // Gate only genuinely approvable (PendingReview) proposals: for any other status the
            // domain transition's terminal-status short-circuit owns the response (409 "Cannot
            // approve proposal in status X"), which this slice leaves untouched — running these
            // gates on a terminal proposal would wrongly report a 400/403/404 in place of that 409.
            if (proposal.Status == ProposalStatus.PendingReview)
            {
                var effectiveOperations = await ResolveEffectiveOperationsAsync(proposal, cancellationToken);
                if (!effectiveOperations.IsSuccess)
                    return Result.Failure<ProposalDto>(effectiveOperations.ErrorCode, effectiveOperations.ErrorMessage);

                var structureValidation = ProposalOperationStructureValidator.Validate(effectiveOperations.Value);
                if (!structureValidation.IsSuccess)
                    return Result.Failure<ProposalDto>(structureValidation.ErrorCode, structureValidation.ErrorMessage);

                if (!proposal.IsExpired)
                {
                    var permissionValidation = await _policyEngine.ValidatePermissionsAsync(
                        proposal.RequestedByUserId,
                        proposal.BoardId,
                        effectiveOperations.Value,
                        cancellationToken);
                    if (!permissionValidation.IsSuccess)
                        return Result.Failure<ProposalDto>(permissionValidation.ErrorCode, permissionValidation.ErrorMessage);
                }
            }

            proposal.Approve(decidedByUserId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "approved", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    /// <summary>
    /// Resolves the effective operation set Apply will execute, for the approve-time structure
    /// and permission/contract gates — the latest saved <see cref="ProposalRevision"/> when one exists (mirroring
    /// <c>AutomationExecutorService.MaterializeEffectiveProposalAsync</c> and the revision-aware
    /// <see cref="GetProposalDiffAsync"/> path), otherwise the proposal's original operations —
    /// so approve validates exactly what Apply will run (#1416 approve == apply). A revision is
    /// structure-validated at save time, so the parse-failure branch is defensive: if the
    /// effective payload cannot be materialized, Apply would fail the same way, so surface the
    /// identical <see cref="ErrorCodes.ValidationError"/>.
    /// </summary>
    private async Task<Result<IReadOnlyCollection<ProposalOperationDto>>> ResolveEffectiveOperationsAsync(
        AutomationProposal proposal,
        CancellationToken cancellationToken)
    {
        var latestRevision = await _unitOfWork.ProposalRevisions.GetLatestByProposalIdAsync(proposal.Id, cancellationToken);
        if (latestRevision is not null)
        {
            if (!ProposalRevisionPayload.TryParseOperations(
                    proposal.Id,
                    latestRevision.RevisedPayload,
                    out var revisedOperations,
                    out var errorMessage))
            {
                return Result.Failure<IReadOnlyCollection<ProposalOperationDto>>(ErrorCodes.ValidationError, errorMessage);
            }

            return Result.Success<IReadOnlyCollection<ProposalOperationDto>>(revisedOperations);
        }

        var originalOperations = proposal.Operations
            .OrderBy(o => o.Sequence)
            .Select(MapOperationToDto)
            .ToList();
        return Result.Success<IReadOnlyCollection<ProposalOperationDto>>(originalOperations);
    }

    public async Task<Result<ProposalDto>> RejectProposalAsync(Guid id, Guid decidedByUserId, UpdateProposalStatusDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.Reject(decidedByUserId, dto.Reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "rejected", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> DeferProposalAsync(Guid id, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.Defer(duration);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Defer is a self-initiated timing control, not a decision: deliberately no
            // ProposalOutcome (outcomes are terminal-decision telemetry) and no notification
            // (a snooze the reviewer initiated is noise, not news).
            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            // Domain guards surface here as ValidationError (400) / InvalidOperation (409).
            // A concurrent decide+defer or double-submit collides on the UpdatedAt concurrency
            // token; UnitOfWork.SaveChangesAsync maps that DbUpdateConcurrencyException to
            // DomainException(Conflict) → 409, so it never escapes as a 500.
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> MarkAsAppliedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.MarkAsApplied();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "applied", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> MarkAsFailedAsync(Guid id, string failureReason, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.MarkAsFailed(failureReason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "failed", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<int>> ExpireProposalsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var expiredProposals = await _unitOfWork.AutomationProposals.GetExpiredAsync(cancellationToken);
            int count = 0;

            foreach (var proposal in expiredProposals)
            {
                proposal.Expire();
                count++;
            }

            if (count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                foreach (var proposal in expiredProposals)
                {
                    var notifyResult = await PublishProposalOutcomeNotificationAsync(
                        proposal,
                        "expired",
                        cancellationToken);
                    if (!notifyResult.IsSuccess)
                        return Result.Failure<int>(notifyResult.ErrorCode, notifyResult.ErrorMessage);
                }
            }

            return Result.Success(count);
        }
        catch (DomainException ex)
        {
            return Result.Failure<int>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<string>> GetProposalDiffAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
        if (proposal == null)
            return Result.Failure<string>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

        // When a reviewer has saved a revision, Apply executes THAT payload — the
        // executor materializes the latest ProposalRevision via
        // AutomationExecutorService.MaterializeEffectiveProposalAsync, not the
        // original operations. Build the diff from the same effective operations so
        // the approval-gate preview equals what Apply will run (#1235). The stored
        // DiffPreview is deliberately bypassed on this path because it describes the
        // original proposal, which is exactly the stale-preview bug we are fixing.
        var latestRevision = await _unitOfWork.ProposalRevisions.GetLatestByProposalIdAsync(id, cancellationToken);
        if (latestRevision is not null)
        {
            if (!ProposalRevisionPayload.TryParseOperations(
                    id,
                    latestRevision.RevisedPayload,
                    out var revisedOperations,
                    out var errorMessage))
            {
                // A saved revision is validated when it is created, so this is
                // defensive: if the effective payload cannot be materialized, Apply
                // would fail the same way — surface that rather than a stale diff.
                return Result.Failure<string>(ErrorCodes.ValidationError, errorMessage);
            }

            // Apply materializes this same revised payload and runs it through
            // AutomationPolicyEngine.ValidatePolicy (structure gate, then expiry) before
            // executing. Mirror both gates here in the same order so a revised proposal that
            // Apply would reject cannot preview a clean diff first (#1376 preview == apply).
            var revisedStructureValidation = ProposalOperationStructureValidator.Validate(revisedOperations);
            if (!revisedStructureValidation.IsSuccess)
                return Result.Failure<string>(revisedStructureValidation.ErrorCode, revisedStructureValidation.ErrorMessage);

            var revisedExpiryValidation = ValidateProposalNotExpired(proposal);
            if (!revisedExpiryValidation.IsSuccess)
                return Result.Failure<string>(revisedExpiryValidation.ErrorCode, revisedExpiryValidation.ErrorMessage);

            // Apply runs AutomationPolicyEngine.ValidatePermissionsAsync AFTER the policy gate
            // (requester exists → 404, board exists → 404, requester board access → 403), and it
            // ends by running the same operation-contract validation. Call that same engine method
            // here so a proposal whose requester lost board access, or whose board/requester was
            // deleted mid-review, cannot preview a clean diff and then fail Apply after approval
            // (#1398 preview == apply). Ordering (structure → expiry → permissions+contract) matches
            // Apply's ValidatePolicy-then-ValidatePermissionsAsync sequence exactly.
            var revisedValidation = await _policyEngine.ValidatePermissionsAsync(
                proposal.RequestedByUserId,
                proposal.BoardId,
                revisedOperations,
                cancellationToken);
            if (!revisedValidation.IsSuccess)
                return Result.Failure<string>(revisedValidation.ErrorCode, revisedValidation.ErrorMessage);

            var revisedViews = revisedOperations
                .OrderBy(o => o.Sequence)
                .Select(o => new DiffOperationView(o.Sequence, o.ActionType, o.TargetType, o.TargetId, o.Parameters))
                .ToList();

            var revisedDiff = await BuildReadableDiffAsync(proposal.BoardId, revisedViews, cancellationToken);
            return Result.Success(revisedDiff);
        }

        var originalOperations = proposal.Operations
            .OrderBy(o => o.Sequence)
            .Select(MapOperationToDto)
            .ToList();

        // Run the same structure invariants Apply enforces (op count, unique/non-negative
        // sequences, parameter size) before building the diff, so a proposal that would be
        // rejected at Apply cannot preview cleanly first (#1370 preview == apply). Apply runs
        // this via AutomationPolicyEngine.ValidatePolicy; mirror it here on the original path.
        // A zero-operation proposal fails here with the same "Proposal must contain at least
        // one operation" ValidationError Apply returns — previously this path returned the
        // cached DiffPreview (200) or a 404, previewing a proposal Apply always rejects
        // (#1376 preview == apply). Structure runs before expiry to match ValidatePolicy's
        // order, so a proposal that is both empty and expired reports the empty error on both.
        var structureValidation = ProposalOperationStructureValidator.Validate(originalOperations);
        if (!structureValidation.IsSuccess)
            return Result.Failure<string>(structureValidation.ErrorCode, structureValidation.ErrorMessage);

        // Apply re-checks expiry in ValidatePolicy after the structure gate; mirror it here —
        // including ahead of the cached-DiffPreview fast path below — so an expired proposal
        // cannot preview a clean diff and then fail Apply after approval (#1376).
        var expiryValidation = ValidateProposalNotExpired(proposal);
        if (!expiryValidation.IsSuccess)
            return Result.Failure<string>(expiryValidation.ErrorCode, expiryValidation.ErrorMessage);

        // Apply runs AutomationPolicyEngine.ValidatePermissionsAsync AFTER the policy gate
        // (requester exists → 404, board exists → 404, requester board access → 403), then the
        // same operation-contract validation. Call that same engine method here — ahead of the
        // cached-DiffPreview fast path below — so a revoked-access or deleted-board/requester
        // proposal cannot preview a clean diff (even a stored one) and then fail Apply after
        // approval (#1398 preview == apply). Structure → expiry → permissions+contract mirrors
        // Apply's ValidatePolicy-then-ValidatePermissionsAsync order exactly.
        var originalValidation = await _policyEngine.ValidatePermissionsAsync(
            proposal.RequestedByUserId,
            proposal.BoardId,
            originalOperations,
            cancellationToken);
        if (!originalValidation.IsSuccess)
            return Result.Failure<string>(originalValidation.ErrorCode, originalValidation.ErrorMessage);

        if (!string.IsNullOrWhiteSpace(proposal.DiffPreview))
            return Result.Success(proposal.DiffPreview);

        var orderedViews = originalOperations
            .Select(o => new DiffOperationView(o.Sequence, o.ActionType, o.TargetType, o.TargetId, o.Parameters))
            .ToList();

        var generatedDiff = await BuildReadableDiffAsync(proposal.BoardId, orderedViews, cancellationToken);
        return Result.Success(generatedDiff);
    }

    /// <summary>
    /// Enforces the same expiry gate Apply runs via
    /// <see cref="AutomationPolicyEngine.ValidatePolicy"/>: an expired proposal is rejected
    /// with the identical <see cref="ErrorCodes.ValidationError"/> / "Proposal has expired"
    /// shape. Diff callers run this after the structure gate (matching ValidatePolicy's order)
    /// so preview rejects exactly what Apply would reject (#1376 preview == apply).
    /// </summary>
    private static Result ValidateProposalNotExpired(AutomationProposal proposal)
        => proposal.IsExpired
            ? Result.Failure(ErrorCodes.ValidationError, "Proposal has expired")
            : Result.Success();

    /// <summary>
    /// Builds the human-readable multi-line diff for an ordered operation set,
    /// resolving column/card IDs to names via a best-effort board lookup. Shared by
    /// the original-operations path and the revision-aware path so both render
    /// identically (#1235).
    /// </summary>
    private async Task<string> BuildReadableDiffAsync(
        Guid? boardId,
        IReadOnlyList<DiffOperationView> orderedOperations,
        CancellationToken cancellationToken)
    {
        // Batch-load entity names for resolving IDs to human-readable labels
        var columnNames = new Dictionary<Guid, string>();
        var cardTitles = new Dictionary<Guid, string>();
        var labelNames = new Dictionary<Guid, string>();

        if (boardId.HasValue)
        {
            try
            {
                var columns = await _unitOfWork.Columns.GetByBoardIdAsync(boardId.Value, cancellationToken);
                foreach (var column in columns)
                    columnNames[column.Id] = column.Name;

                var cards = await _unitOfWork.Cards.GetByBoardIdAsync(boardId.Value, cancellationToken);
                foreach (var card in cards)
                    cardTitles[card.Id] = card.Title;

                var labels = await _unitOfWork.Labels.GetByBoardIdAsync(boardId.Value, cancellationToken);
                foreach (var label in labels)
                    labelNames[label.Id] = label.Name;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Non-critical: if lookups fail, fall back to IDs
            }
        }

        return string.Join(
            Environment.NewLine,
            orderedOperations.Select(o => DescribeOperationReadable(o, columnNames, cardTitles, labelNames)));
    }

    public async Task<Result<int>> DismissProposalsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return Result.Success(0);

        try
        {
            var proposals = await _unitOfWork.AutomationProposals.GetByIdsAsync(ids, cancellationToken);
            int dismissed = 0;

            foreach (var proposal in proposals)
            {
                if (proposal.CanBeDismissed)
                {
                    proposal.Dismiss();
                    dismissed++;
                }
                // Skip proposals not in a dismissible state
            }

            if (dismissed > 0)
                await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(dismissed);
        }
        catch (DomainException ex)
        {
            return Result.Failure<int>(ex.ErrorCode, ex.Message);
        }
    }

    private static ProposalDto MapToDto(AutomationProposal proposal)
    {
        return new ProposalDto(
            proposal.Id,
            proposal.SourceType,
            proposal.SourceReferenceId,
            proposal.BoardId,
            proposal.RequestedByUserId,
            proposal.Status,
            proposal.RiskLevel,
            proposal.Summary,
            proposal.DiffPreview,
            proposal.ValidationIssues,
            proposal.CreatedAt,
            proposal.UpdatedAt,
            proposal.ExpiresAt,
            proposal.DecidedAt,
            proposal.DecidedByUserId,
            proposal.AppliedAt,
            proposal.FailureReason,
            proposal.CorrelationId,
            proposal.Operations.Select(MapOperationToDto).ToList()
        )
        {
            Presentation = BuildPresentation(proposal),
            IsExpired = proposal.IsExpired,
            DeferredUntil = proposal.DeferredUntil
        };
    }

    private static ProposalOperationDto MapOperationToDto(AutomationProposalOperation operation)
    {
        return new ProposalOperationDto(
            operation.Id,
            operation.ProposalId,
            operation.Sequence,
            operation.ActionType,
            operation.TargetType,
            operation.TargetId,
            operation.Parameters,
            operation.IdempotencyKey,
            operation.ExpectedVersion
        );
    }

    private async Task<Result> PublishProposalOutcomeNotificationAsync(
        AutomationProposal proposal,
        string outcome,
        CancellationToken cancellationToken)
    {
        var publishResult = await _notificationService.PublishAsync(
            new CreateNotificationRequestDto(
                proposal.RequestedByUserId,
                NotificationType.ProposalOutcome,
                "Automation proposal updated",
                $"Your proposal '{proposal.Summary}' is now {outcome}.",
                proposal.BoardId,
                SourceEntityType: "proposal",
                SourceEntityId: proposal.Id,
                DeduplicationKey: $"proposal:{proposal.Id}:{proposal.Status}"),
            cancellationToken);

        if (!publishResult.IsSuccess)
            return Result.Failure(publishResult.ErrorCode, publishResult.ErrorMessage);

        return Result.Success();
    }

    private static ProposalPresentationDto BuildPresentation(AutomationProposal proposal)
    {
        var orderedOperations = proposal.Operations
            .OrderBy(operation => operation.Sequence)
            .ToList();

        var affectedEntities = orderedOperations
            .GroupBy(operation => new
            {
                EntityType = HumanizeTargetType(operation.TargetType),
                operation.TargetId
            })
            .Select(group => new ProposalAffectedEntityDto(
                group.Key.EntityType,
                group.Key.TargetId,
                BuildAffectedEntityLabel(
                    group.Key.EntityType,
                    group.Key.TargetId,
                    group.Select(op => ExtractNamedTarget(op.Parameters)).FirstOrDefault(name => name is not null)),
                group.Count()))
            .ToList();

        var operationHeadlines = orderedOperations
            .Select(DescribeOperation)
            .ToList();

        var isCaptureTaskBatch = IsCaptureTaskBatch(proposal.SourceType, orderedOperations);

        return new ProposalPresentationDto(
            BuildPlainSummary(proposal.Summary, isCaptureTaskBatch, orderedOperations, affectedEntities),
            BuildImpactSummary(orderedOperations.Count, affectedEntities, isCaptureTaskBatch),
            BuildRiskCue(proposal.RiskLevel),
            BuildSourceCue(proposal.SourceType),
            operationHeadlines,
            affectedEntities);
    }

    private static string BuildPlainSummary(
        string summary,
        bool isCaptureTaskBatch,
        IReadOnlyList<AutomationProposalOperation> orderedOperations,
        IReadOnlyList<ProposalAffectedEntityDto> affectedEntities)
    {
        if (orderedOperations.Count == 0)
        {
            return summary;
        }

        if (orderedOperations.Count == 1)
        {
            return $"{summary} This would {LowercaseSentenceLead(DescribeOperation(orderedOperations[0]))}";
        }

        if (isCaptureTaskBatch)
        {
            return $"Create {orderedOperations.Count} task card{Pluralize(orderedOperations.Count)} from the captured note.";
        }

        var entitySummary = affectedEntities.Count switch
        {
            0 => "this workspace",
            1 => affectedEntities[0].Label.ToLowerInvariant(),
            _ => string.Join(", ", affectedEntities.Take(2).Select(entity => entity.EntityType.ToLowerInvariant()))
        };

        return $"{summary} This would apply {orderedOperations.Count} planned changes across {entitySummary}.";
    }

    private static string BuildImpactSummary(int operationCount, IReadOnlyList<ProposalAffectedEntityDto> affectedEntities, bool isCaptureTaskBatch)
    {
        if (operationCount == 0)
        {
            return "No concrete board operations were attached to this proposal.";
        }

        if (isCaptureTaskBatch &&
            affectedEntities.Count == 1 &&
            string.Equals(affectedEntities[0].EntityType, "Card", StringComparison.OrdinalIgnoreCase) &&
            affectedEntities[0].ChangeCount == operationCount)
        {
            return $"{operationCount} task card change{Pluralize(operationCount)} ready for approval.";
        }

        if (affectedEntities.Count == 0)
        {
            return $"{operationCount} change{Pluralize(operationCount)} planned.";
        }

        return $"{operationCount} change{Pluralize(operationCount)} touching {affectedEntities.Count} target surface{Pluralize(affectedEntities.Count)}.";
    }

    private static string BuildRiskCue(RiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RiskLevel.Low => "Low risk. Usually safe to review quickly.",
            RiskLevel.Medium => "Medium risk. Check the affected items before approving.",
            RiskLevel.High => "High risk. Review the affected items and execution order carefully.",
            RiskLevel.Critical => "Critical risk. Treat this as a high-trust change and verify every step.",
            _ => "Review the proposed changes before approving."
        };
    }

    private static string BuildSourceCue(ProposalSourceType sourceType)
    {
        return sourceType switch
        {
            ProposalSourceType.Queue => "Created from Inbox capture triage.",
            ProposalSourceType.Chat => "Created from an automation chat session.",
            ProposalSourceType.Manual => "Created manually from an operator-driven proposal flow.",
            _ => "Created from a review-first automation flow."
        };
    }

    private static string DescribeOperation(AutomationProposalOperation operation)
    {
        var verb = HumanizeActionVerb(operation.ActionType);
        var target = HumanizeTargetType(operation.TargetType).ToLowerInvariant();
        var namedTarget = ExtractNamedTarget(operation.Parameters);

        return namedTarget is null
            ? $"{verb} {target}."
            : $"{verb} {target} \"{namedTarget}\".";
    }

    /// <summary>
    /// Lightweight, source-agnostic view of a proposal operation used for diff
    /// rendering. Both the entity operations and the revised-payload DTO operations
    /// map onto this so the readable diff renders identically regardless of source (#1235).
    /// </summary>
    private readonly record struct DiffOperationView(
        int Sequence,
        string ActionType,
        string TargetType,
        string? TargetId,
        string Parameters);

    /// <summary>
    /// Produces a human-readable diff line for a single operation, resolving
    /// card IDs to titles and column IDs to names where possible.
    /// </summary>
    private static string DescribeOperationReadable(
        DiffOperationView operation,
        IReadOnlyDictionary<Guid, string> columnNames,
        IReadOnlyDictionary<Guid, string> cardTitles,
        IReadOnlyDictionary<Guid, string> labelNames)
    {
        var verb = HumanizeActionVerb(operation.ActionType);
        var targetType = HumanizeTargetType(operation.TargetType).ToLowerInvariant();
        var isCardTarget = string.Equals(operation.TargetType, "card", StringComparison.OrdinalIgnoreCase);
        var labelAction = CardLabelOperationVocabulary.Classify(operation.ActionType);
        var isLabelOperation = isCardTarget &&
            labelAction is CardLabelOperationAction.Add or CardLabelOperationAction.Remove;
        var namedTarget = isLabelOperation ? null : ExtractNamedTarget(operation.Parameters);

        // Try to resolve card title from lookup when not embedded in parameters
        // Only attempt card-specific lookups when the operation targets a card
        if (namedTarget is null && isCardTarget && !string.IsNullOrWhiteSpace(operation.TargetId))
        {
            if (Guid.TryParse(operation.TargetId, out var targetGuid) && cardTitles.TryGetValue(targetGuid, out var title))
                namedTarget = title;
        }

        // Also try to resolve card title from cardId parameter
        if (namedTarget is null && isCardTarget)
        {
            var cardIdFromParams = ExtractGuidParameter(operation.Parameters, "cardId");
            if (cardIdFromParams.HasValue && cardTitles.TryGetValue(cardIdFromParams.Value, out var title))
                namedTarget = title;
        }

        if (isLabelOperation)
        {
            var labelName = ExtractStringParameter(operation.Parameters, "labelName");
            var labelId = ExtractGuidParameter(operation.Parameters, "labelId");
            var labelDisplay = labelName is not null
                ? $"\"{labelName}\""
                : labelId.HasValue
                    ? DescribeLabel(labelId.Value, labelNames)
                    : "(unspecified)";
            var cardDisplay = namedTarget is not null
                ? $"\"{namedTarget}\""
                : !string.IsNullOrWhiteSpace(operation.TargetId)
                    ? operation.TargetId
                    : ExtractGuidParameter(operation.Parameters, "cardId")?.ToString() ?? "(unspecified)";
            var preposition = labelAction == CardLabelOperationAction.Add ? "to" : "from";
            return $"{operation.Sequence}. {verb} label {labelDisplay} {preposition} card {cardDisplay}";
        }

        // Column reorder: surface the CLAMPED effective destination so the approval
        // preview shows what Apply will do (the position is the whole point of the op).
        // ColumnService.ReorderColumnAsync inserts at Math.Min(position, columnCount - 1),
        // so an overshooting target silently lands at the end. Mirror that clamp against
        // the current board columns so preview == apply (#1370); when the board columns
        // are unknown (best-effort lookup failed) fall back to the requested value.
        if (string.Equals(operation.TargetType, "column", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.ActionType, "reorder", StringComparison.OrdinalIgnoreCase))
        {
            var reorderColumnId = ExtractGuidParameter(operation.Parameters, "columnId")
                ?? (Guid.TryParse(operation.TargetId, out var reorderTargetId) ? reorderTargetId : (Guid?)null);
            var reorderColumnDisplay = reorderColumnId.HasValue && columnNames.TryGetValue(reorderColumnId.Value, out var reorderColumnName)
                ? $"\"{reorderColumnName}\""
                : reorderColumnId?.ToString() ?? "(unspecified)";
            var reorderPosition = ExtractInt32Parameter(operation.Parameters, "position");
            if (!reorderPosition.HasValue)
                return $"{operation.Sequence}. {verb} column {reorderColumnDisplay}";

            var effectivePosition = columnNames.Count > 0
                ? Math.Min(reorderPosition.Value, columnNames.Count - 1)
                : reorderPosition.Value;
            return $"{operation.Sequence}. {verb} column {reorderColumnDisplay} to position {effectivePosition}";
        }

        // Build description, falling back to raw TargetId when no name is available
        var description = namedTarget is not null
            ? $"{operation.Sequence}. {verb} {targetType} \"{namedTarget}\""
            : !string.IsNullOrWhiteSpace(operation.TargetId)
                ? $"{operation.Sequence}. {verb} {targetType} {operation.TargetId}"
                : $"{operation.Sequence}. {verb} {targetType}";

        // Append column context for operations that reference a column
        var columnId = ExtractGuidParameter(operation.Parameters, "columnId");
        if (columnId.HasValue)
        {
            var columnDisplay = columnNames.TryGetValue(columnId.Value, out var columnName)
                ? $"\"{columnName}\""
                : columnId.Value.ToString();

            if (verb == "Move")
                description += $" to column {columnDisplay}";
            else if (verb == "Create")
                description += $" in column {columnDisplay}";
        }

        var cardEffects = DescribeCardParameterEffects(operation.Parameters, labelNames);
        if (isCardTarget && cardEffects.Count > 0)
            description += $"; {string.Join("; ", cardEffects)}";

        return description;
    }

    private static IReadOnlyList<string> DescribeCardParameterEffects(
        string parameters,
        IReadOnlyDictionary<Guid, string> labelNames)
    {
        if (!OperationParameterParser.TryDeserializeParameters(parameters, out var parsed, out _))
            return Array.Empty<string>();

        var effects = new List<string>();
        if (OperationParameterParser.TryGetOptionalDateTimeOffset(
                parsed, "dueDate", out var dueDateProvided, out var dueDate, out _)
            && dueDateProvided)
        {
            effects.Add(dueDate.HasValue
                ? $"set due date to {dueDate.Value:O}"
                : "clear due date");
        }

        if (parsed.TryGetProperty("clearDueDate", out var clearProperty)
            && clearProperty.ValueKind == JsonValueKind.True)
        {
            effects.RemoveAll(effect => effect.StartsWith("set due date", StringComparison.Ordinal));
            if (!effects.Contains("clear due date", StringComparer.Ordinal))
                effects.Add("clear due date");
        }

        if (OperationParameterParser.TryGetOptionalStringArray(
                parsed, "labels", out var labelsProvided, out var labels, out _)
            && labelsProvided)
        {
            var effectiveLabels = labels.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            effects.Add(effectiveLabels.Count == 0
                ? "replace labels with none"
                : $"replace labels with [{string.Join(", ", effectiveLabels.Select(label => $"\"{label}\""))}]");
        }

        if (OperationParameterParser.TryGetOptionalGuidArray(
                parsed, "labelIds", out var labelIdsProvided, out var labelIds, out _)
            && labelIdsProvided)
        {
            var effectiveLabelIds = labelIds.Distinct().ToList();
            effects.Add(effectiveLabelIds.Count == 0
                ? "replace labels with none"
                : $"replace labels with [{string.Join(", ", effectiveLabelIds.Select(labelId => DescribeLabel(labelId, labelNames)))}]");
        }

        return effects;
    }

    private static string DescribeLabel(Guid labelId, IReadOnlyDictionary<Guid, string> labelNames)
    {
        return labelNames.TryGetValue(labelId, out var labelName)
            ? $"\"{labelName}\""
            : labelId.ToString();
    }

    /// <summary>
    /// Extracts a GUID value from a JSON parameters string by property name.
    /// Returns null when the property is missing, not a valid GUID, or the JSON is invalid.
    /// </summary>
    private static Guid? ExtractGuidParameter(string parameters, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return null;

        try
        {
            using var document = JsonDocument.Parse(parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty(propertyName, out var propertyValue))
                return null;

            if (propertyValue.TryGetGuid(out var guidValue))
                return guidValue;
        }
        catch (JsonException)
        {
            // Malformed JSON — fall through to null
        }

        return null;
    }

    private static int? ExtractInt32Parameter(string parameters, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return null;

        try
        {
            using var document = JsonDocument.Parse(parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (document.RootElement.TryGetProperty(propertyName, out var propertyValue)
                && propertyValue.ValueKind == JsonValueKind.Number
                && propertyValue.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Malformed JSON — fall through to null
        }

        return null;
    }

    private static string? ExtractStringParameter(string parameters, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return null;

        try
        {
            using var document = JsonDocument.Parse(parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(propertyName, out var propertyValue)
                || propertyValue.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = propertyValue.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractNamedTarget(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propertyName in new[] { "title", "name", "boardName", "columnName", "labelName" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var propertyValue) &&
                    propertyValue.ValueKind == JsonValueKind.String)
                {
                    var value = propertyValue.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string HumanizeActionVerb(string actionType)
    {
        var normalized = actionType
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();

        if (normalized.Length == 0)
        {
            return "Update";
        }

        var tokens = SplitPascalCase(normalized)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var preferredVerb = tokens.FirstOrDefault(token => KnownActionVerbs.Contains(token))
            ?? tokens.LastOrDefault(token => KnownActionVerbs.Contains(token))
            ?? tokens.FirstOrDefault(token => token.All(char.IsLetter))
            ?? tokens.First();
        return char.ToUpperInvariant(preferredVerb[0]) + preferredVerb[1..].ToLowerInvariant();
    }

    private static string HumanizeTargetType(string targetType)
    {
        if (string.IsNullOrWhiteSpace(targetType))
        {
            return "Item";
        }

        var normalized = targetType
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();

        var humanized = SplitPascalCase(normalized)
            .Replace("  ", " ")
            .Trim();

        return humanized.Length == 0
            ? "Item"
            : char.ToUpperInvariant(humanized[0]) + humanized[1..];
    }

    private static string LowercaseSentenceLead(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return sentence;
        }

        return char.ToLowerInvariant(sentence[0]) + sentence[1..];
    }

    private static string BuildAffectedEntityLabel(string entityType, string? entityId, string? namedTarget)
    {
        if (!string.IsNullOrWhiteSpace(namedTarget))
        {
            return $"{entityType} \"{namedTarget}\"";
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            return entityType;
        }

        return $"{entityType} {entityId}";
    }

    private static string SplitPascalCase(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length * 2);

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[index - 1]))
            {
                buffer.Append(' ');
            }

            buffer.Append(current);
        }

        return buffer.ToString();
    }

    private static bool IsCaptureTaskBatch(ProposalSourceType sourceType, IReadOnlyList<AutomationProposalOperation> orderedOperations)
    {
        if (sourceType != ProposalSourceType.Queue)
        {
            return false;
        }

        if (orderedOperations.Count < 2)
        {
            return false;
        }

        return orderedOperations.All(operation =>
            string.Equals(operation.ActionType, CaptureTriageActionType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.TargetType, CaptureTriageTargetType, StringComparison.OrdinalIgnoreCase));
    }

    private static string Pluralize(int count) => count == 1 ? string.Empty : "s";
}
