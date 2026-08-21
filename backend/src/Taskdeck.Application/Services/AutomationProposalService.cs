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

    public Task<Result<ProposalDto>> CreateProposalAsync(CreateProposalDto dto, CancellationToken cancellationToken = default)
        => CreateProposalCoreAsync(dto, evidence: null, cancellationToken);

    public async Task<Result<ProposalDto>> CreateTranscriptProposalAsync(
        CreateProposalDto dto,
        IReadOnlyList<TranscriptEvidenceLinkInput> evidence,
        CancellationToken cancellationToken = default)
    {
        if (_provenanceRepository is null)
        {
            return Result.Failure<ProposalDto>(
                ErrorCodes.UnexpectedError,
                "Transcript evidence persistence is unavailable");
        }

        var evidenceValidation = ValidateTranscriptEvidence(dto, evidence);
        if (!evidenceValidation.IsSuccess)
        {
            return Result.Failure<ProposalDto>(
                evidenceValidation.ErrorCode,
                evidenceValidation.ErrorMessage);
        }

        return await CreateProposalCoreAsync(dto, evidence, cancellationToken);
    }

    private async Task<Result<ProposalDto>> CreateProposalCoreAsync(
        CreateProposalDto dto,
        IReadOnlyList<TranscriptEvidenceLinkInput>? evidence,
        CancellationToken cancellationToken)
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
                var provenance = BuildCreationProvenance(proposal, dto, evidence);
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

    private static ProposalProvenance BuildCreationProvenance(
        AutomationProposal proposal,
        CreateProposalDto dto,
        IReadOnlyList<TranscriptEvidenceLinkInput>? evidence)
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

        var evidenceBySequence = evidence?.ToDictionary(item => item.OperationSequence);
        for (var i = 0; i < orderedOperations.Count; i++)
        {
            var operation = orderedOperations[i];
            var field = new ProvenanceField(
                TruncateProvenanceFieldName($"Operation {i + 1}: {operation.ActionType} {operation.TargetType}"),
                ProvenanceKind.Inferred,
                0.75,
                provenance.Id);
            if (evidenceBySequence is not null)
            {
                var link = evidenceBySequence[operation.Sequence];
                field.AddEvidenceLink(new ProvenanceEvidenceLink(
                    ProvenanceEvidenceLink.TranscriptSourceType,
                    link.TranscriptId.ToString("D"),
                    field.Id,
                    label: "Transcript evidence",
                    spanStart: link.SpanStart,
                    spanEnd: link.SpanEnd,
                    transcriptId: link.TranscriptId));
            }

            provenance.AddField(field);
        }

        return provenance;
    }

    private static Result ValidateTranscriptEvidence(
        CreateProposalDto dto,
        IReadOnlyList<TranscriptEvidenceLinkInput>? evidence)
    {
        if (evidence is null || dto.Operations is null || evidence.Count != dto.Operations.Count)
        {
            return Result.Failure(ErrorCodes.ValidationError, "Transcript evidence must cover every proposal operation exactly once");
        }

        var operationSequences = dto.Operations.Select(operation => operation.Sequence).ToList();
        if (operationSequences.Count != operationSequences.Distinct().Count())
        {
            return Result.Failure(ErrorCodes.ValidationError, "Proposal operation sequences must be unique");
        }

        var evidenceSequences = evidence.Select(item => item.OperationSequence).ToList();
        if (evidenceSequences.Count != evidenceSequences.Distinct().Count() ||
            !operationSequences.OrderBy(sequence => sequence).SequenceEqual(evidenceSequences.OrderBy(sequence => sequence)))
        {
            return Result.Failure(ErrorCodes.ValidationError, "Transcript evidence must match proposal operation sequences");
        }

        foreach (var link in evidence)
        {
            if (link.TranscriptId == Guid.Empty)
            {
                return Result.Failure(ErrorCodes.ValidationError, "Transcript evidence requires a transcript ID");
            }

            if (link.SpanStart.HasValue != link.SpanEnd.HasValue)
            {
                return Result.Failure(ErrorCodes.ValidationError, "Transcript evidence span offsets must be paired");
            }

            if (link.SpanStart is { } start && link.SpanEnd is { } end &&
                (start < 0 || end <= start))
            {
                return Result.Failure(ErrorCodes.ValidationError, "Transcript evidence span offsets must be non-empty and ordered");
            }
        }

        if (evidence.Select(link => link.TranscriptId).Distinct().Count() != 1)
        {
            return Result.Failure(ErrorCodes.ValidationError, "Transcript evidence must reference one transcript");
        }

        return Result.Success();
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

        // Surface the EFFECTIVE operation set (pinned/latest revision when one applies, else the
        // originals) so a revised proposal no longer echoes stale original operations (#1424).
        return await BuildEffectiveProposalDtoAsync(proposal, cancellationToken);
    }

    public async Task<Result<IEnumerable<ProposalDto>>> GetProposalsAsync(ProposalFilterDto? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new ProposalFilterDto();
        var limit = filter.Limit <= 0 ? 100 : filter.Limit;

        IEnumerable<AutomationProposal> proposals;

        // Apply filters in order of specificity
        if (filter.UserId.HasValue)
        {
            if (!filter.BoardId.HasValue)
            {
                // The unscoped Review queue is an active-work surface. Archived-board history is
                // retained but excluded before the repository applies LIMIT; status/risk are also
                // pushed into that bounded query so hidden history cannot under-fill the page.
                proposals = await _unitOfWork.AutomationProposals.GetActiveByUserIdAsync(
                    filter.UserId.Value,
                    limit,
                    filter.Status,
                    filter.RiskLevel,
                    cancellationToken);
            }
            else
            {
                // Explicit board history (including archived boards) remains available. Keep the
                // user predicate first for MCP callers that supply both UserId and BoardId.
                proposals = await _unitOfWork.AutomationProposals.GetByUserIdAsync(
                    filter.UserId.Value,
                    limit,
                    cancellationToken: cancellationToken);
            }
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

        // Materialize the page ONCE before the revision read. The chain above is lazy, so the batch
        // lookup and the DTO projection must be fed from the same materialized list — re-enumerating
        // it would re-run every filter and could even resolve revisions for a different set of rows.
        var page = proposals.Take(limit).ToList();

        // Resolve the effective revision for the whole page in a single revision query (#1444), so a
        // review-queue card can no longer show the original summary/operations while the detail view,
        // the diff and Apply all use the revised set. Absent from the dictionary means "no effective
        // revision" — exactly the null the single-proposal read produces — so the builder maps the
        // proposal's original operations for those items.
        var effectiveRevisions = await GetEffectiveRevisionsAsync(page, cancellationToken);

        var dtos = page
            .Select(proposal => BuildEffectiveProposalDto(
                proposal,
                effectiveRevisions.TryGetValue(proposal.Id, out var revision) ? revision : null))
            .ToList();

        return Result.Success<IEnumerable<ProposalDto>>(dtos);
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
            //      via the same _policyEngine.ValidatePermissionsAsync call — and, like Apply, at
            //      the Write bar, because approving commits the reviewer to a board mutation. The
            //      diff path calls the same method at the Read bar (#1836): that is the one
            //      deliberate gate difference between preview and approve/apply, and it can only
            //      make preview MORE permissive, never approve/apply.
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
            Guid? approvedRevisionId = null;
            // Hoisted so the success-path DTO build can reuse the exact revision read here (the
            // pinned one) instead of re-querying it (Gemini review, #1439). Null when the proposal
            // is not PendingReview — but the domain guard in Approve throws for any other status
            // before that DTO is built, so the reused value is always the pinned revision.
            ProposalRevision? latestRevision = null;
            if (proposal.Status == ProposalStatus.PendingReview)
            {
                // Read the latest revision NOW and pin its id onto the proposal (#1428): approve
                // validates this exact revision and Apply materializes it, so a revision saved
                // later — even one landing in the race window between this read and approve's
                // commit — can no longer change what Apply executes. A null id approves the
                // original operations, and Apply then ignores any post-approval revision entirely.
                latestRevision = await _unitOfWork.ProposalRevisions.GetLatestByProposalIdAsync(proposal.Id, cancellationToken);
                approvedRevisionId = latestRevision?.Id;

                var effectiveOperations = ResolveEffectiveGateOperations(proposal, latestRevision);
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
                        BoardAccessBar.Write,
                        cancellationToken);
                    if (!permissionValidation.IsSuccess)
                        return Result.Failure<ProposalDto>(permissionValidation.ErrorCode, permissionValidation.ErrorMessage);
                }
            }

            proposal.Approve(decidedByUserId, approvedRevisionId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "approved", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            // Echo the effective (pinned) operations Apply will run, not the stale originals (#1424).
            // latestRevision IS the pinned revision (its id was stored as ApprovedRevisionId), so map
            // the DTO from it directly rather than re-reading it via GetEffectiveRevisionAsync.
            return Result.Success(BuildEffectiveProposalDto(proposal, latestRevision));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    /// <summary>
    /// Resolves the effective operation set Apply will execute, for the approve-time structure
    /// and permission/contract gates — the supplied latest saved <see cref="ProposalRevision"/>
    /// when one exists (mirroring <c>AutomationExecutorService.MaterializeEffectiveProposalAsync</c>
    /// and the revision-aware <see cref="GetProposalDiffAsync"/> path), otherwise the proposal's
    /// original operations — so approve validates exactly what Apply will run (#1416 approve ==
    /// apply). The revision is passed in (already read in <see cref="ApproveProposalAsync"/> so its
    /// id can be pinned as <see cref="AutomationProposal.ApprovedRevisionId"/>) to avoid a second
    /// query. A revision is structure-validated at save time, so the parse-failure branch is
    /// defensive: if the effective payload cannot be materialized, Apply would fail the same way,
    /// so surface the identical <see cref="ErrorCodes.ValidationError"/>.
    /// </summary>
    private Result<IReadOnlyCollection<ProposalOperationDto>> ResolveEffectiveGateOperations(
        AutomationProposal proposal,
        ProposalRevision? latestRevision)
    {
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

    /// <summary>
    /// Resolves the revision whose operations are the EFFECTIVE set for a proposal — the one Apply
    /// will materialize — so the diff preview and the decided-proposal response DTOs agree with
    /// Apply (preview == apply / approve == apply). A decided proposal with a pinned
    /// <see cref="AutomationProposal.ApprovedRevisionId"/> returns that revision (#1428); an
    /// Approved proposal with a null pin returns null so the original set is used, ignoring any
    /// revision that raced in after approval. A still-pending proposal returns the unconditional
    /// latest saved revision (what the reviewer sees, and what approve would pin). A Rejected
    /// proposal is FROZEN at its decision time: it returns the latest revision saved at or before
    /// <see cref="AutomationProposal.DecidedAt"/>, so a revision that raced in AFTER rejection can
    /// never surface in the reject/GET/diff response (Codex review, #1439). Returns null when no
    /// revision applies, meaning "use the proposal's original operations".
    /// </summary>
    private async Task<ProposalRevision?> GetEffectiveRevisionAsync(
        AutomationProposal proposal,
        CancellationToken cancellationToken)
    {
        if (!CanHaveEffectiveRevision(proposal))
            return null;

        // Two-phase (#1444 review): compare metadata to decide which revision wins, then load only
        // that one's payload. Costs two cheap indexed queries instead of one, and in exchange no read
        // path ever pulls a revision payload it will not use — the Rejected branch previously loaded
        // EVERY payload for the proposal, so for that status this is strictly less work.
        var refs = await _unitOfWork.ProposalRevisions.GetRefsByProposalIdsAsync(
            new[] { proposal.Id },
            cancellationToken);

        if (SelectEffectiveRevisionRef(proposal, refs) is not ProposalRevisionRef winner)
            return null;

        return await _unitOfWork.ProposalRevisions.GetByIdAsync(winner.Id, cancellationToken);
    }

    /// <summary>
    /// Batch equivalent of <see cref="GetEffectiveRevisionAsync"/> for list reads: resolves the
    /// effective revision for a whole page of proposals in ONE revision query instead of one per
    /// proposal (#1444). Proposals that cannot resolve to a revision at all are excluded from the
    /// query, and proposals with no effective revision are simply absent from the result — callers
    /// treat "absent" exactly as <see cref="GetEffectiveRevisionAsync"/>'s null, i.e. use the
    /// proposal's original operations. Selection runs through the same
    /// <see cref="SelectEffectiveRevision"/> rules as the single-proposal path, so the list and the
    /// detail view cannot disagree about which revision is effective.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, ProposalRevision>> GetEffectiveRevisionsAsync(
        IReadOnlyCollection<AutomationProposal> proposals,
        CancellationToken cancellationToken)
    {
        var candidates = proposals.Where(CanHaveEffectiveRevision).ToList();
        if (candidates.Count == 0)
            return new Dictionary<Guid, ProposalRevision>();

        // Phase 1: metadata for the whole page in one read. Payload-free, so page size drives row
        // count but never bytes-per-row (#1444 review).
        var refs = await _unitOfWork.ProposalRevisions.GetRefsByProposalIdsAsync(
            candidates.Select(p => p.Id),
            cancellationToken);

        var refsByProposal = refs
            .GroupBy(r => r.ProposalId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProposalRevisionRef>)g.ToList());

        var winners = new Dictionary<Guid, Guid>();
        foreach (var proposal in candidates)
        {
            if (!refsByProposal.TryGetValue(proposal.Id, out var proposalRefs))
                continue;

            if (SelectEffectiveRevisionRef(proposal, proposalRefs) is ProposalRevisionRef winner)
                winners[proposal.Id] = winner.Id;
        }

        if (winners.Count == 0)
            return new Dictionary<Guid, ProposalRevision>();

        // Phase 2: payloads for the winners only — at most one per proposal, and only for proposals
        // that actually resolved a revision.
        var winningRevisions = await _unitOfWork.ProposalRevisions.GetByIdsAsync(
            winners.Values,
            cancellationToken);
        var byRevisionId = winningRevisions.ToDictionary(r => r.Id);

        var effective = new Dictionary<Guid, ProposalRevision>();
        foreach (var (proposalId, revisionId) in winners)
        {
            // Absent only if the row vanished between the two reads; treat it as "no effective
            // revision", which is the same degradation the single-proposal path applies.
            if (byRevisionId.TryGetValue(revisionId, out var revision))
                effective[proposalId] = revision;
        }

        return effective;
    }

    /// <summary>
    /// True when <paramref name="proposal"/> could resolve to an effective revision at all, letting
    /// read paths skip the revision query entirely for proposals that always use their original
    /// operations (any status other than PendingReview/Rejected without a pin).
    /// <para>
    /// This predicate gates <see cref="SelectEffectiveRevisionRef"/> on BOTH read paths, so
    /// "false here ⇒ original operations" holds by construction rather than by test. The parity tests
    /// pin that the two paths AGREE and what each status resolves to; they cannot pin this
    /// implication, because when the predicate is false the selector is never invoked at all. The
    /// consequence to remember: adding a branch to the selector for a status this predicate excludes
    /// would be dead code, and no test would say so (#1444 review).
    /// </para>
    /// </summary>
    private static bool CanHaveEffectiveRevision(AutomationProposal proposal) =>
        proposal.ApprovedRevisionId is not null
        || proposal.Status is ProposalStatus.PendingReview or ProposalStatus.Rejected;

    /// <summary>
    /// The single implementation of the effective-revision rules, applied to the revision METADATA of
    /// ONE proposal. Both the single-proposal read (<see cref="GetEffectiveRevisionAsync"/>) and the
    /// batched list read (<see cref="GetEffectiveRevisionsAsync"/>) select through this method, so
    /// there is exactly one copy of the rules rather than a duplicate per read shape.
    /// <para>
    /// Takes <see cref="ProposalRevisionRef"/> rather than the entity because the rules only ever
    /// compare revision numbers, timestamps and ids — never the payload. That is what lets callers
    /// avoid loading payloads for revisions that lose (#1444 review).
    /// </para>
    /// <paramref name="refsForProposal"/> must contain only refs of <paramref name="proposal"/>;
    /// ordering within it is irrelevant (selection is explicit).
    /// </summary>
    private static ProposalRevisionRef? SelectEffectiveRevisionRef(
        AutomationProposal proposal,
        IReadOnlyList<ProposalRevisionRef> refsForProposal)
    {
        if (proposal.ApprovedRevisionId is Guid approvedRevisionId)
        {
            // Resolved within the proposal's OWN revisions. A pin is only ever set from a revision of
            // the same proposal (ApproveProposalAsync pins what GetLatestByProposalIdAsync returned),
            // so this agrees with a global by-id lookup for every reachable state while making a
            // cross-proposal id structurally unable to render as this proposal's content.
            //
            // Two asymmetries against AutomationExecutorService.MaterializeEffectiveProposalAsync,
            // stated in full because the containment above is only half the story (#1444 review):
            //  - Scope: the executor resolves the pin GLOBALLY by id and does not check ProposalId.
            //    For a pin pointing at ANOTHER proposal's revision, reads would now fall back to the
            //    originals while Apply would execute the foreign revision — a preview/apply
            //    divergence in a state where the two previously agreed (both used the foreign one).
            //  - Missing row: reads fall back to the original operations, while Apply REFUSES
            //    outright (InvalidOperation) rather than execute an unapproved set.
            // Both states are unreachable: nothing but Approve writes ApprovedRevisionId, and a
            // revision is cascade-owned by its proposal with no code path deleting one individually,
            // so a pin can neither point elsewhere nor dangle while its proposal is readable.
            return refsForProposal.FirstOrDefault(r => r.Id == approvedRevisionId);
        }

        if (proposal.Status is ProposalStatus.PendingReview)
        {
            // Unconditional latest: what the reviewer sees, and what approve would pin. Highest
            // RevisionNumber, matching IProposalRevisionRepository.GetLatestByProposalIdAsync's
            // ordering. Deterministic because (ProposalId, RevisionNumber) is uniquely indexed.
            return refsForProposal.MaxBy(r => r.RevisionNumber);
        }

        if (proposal.Status is ProposalStatus.Rejected)
        {
            // Freeze the rejected proposal at decision time. DecidedAt is always set by Reject, but
            // treat a null defensively as "no cutoff" and fall back to the unconditional latest.
            if (proposal.DecidedAt is not DateTime decidedAt)
                return refsForProposal.MaxBy(r => r.RevisionNumber);

            // Compare in memory rather than relying on EF's SQLite provider to translate a
            // DateTimeOffset-vs-DateTime comparison. RevisedAt is a DateTimeOffset in UTC; compare
            // its UtcDateTime against the UTC DecidedAt.
            return refsForProposal
                .Where(r => r.RevisedAt.UtcDateTime <= decidedAt)
                .OrderByDescending(r => r.RevisedAt)
                .ThenByDescending(r => r.RevisionNumber)
                .FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Maps a proposal to its response DTO with <see cref="ProposalDto.Operations"/> AND
    /// <see cref="ProposalDto.Presentation"/> materialized together from the EFFECTIVE operation
    /// set (the pinned/latest revision when one applies, else the originals), so approve/reject/get
    /// responses no longer echo stale original operations for a revised proposal (#1424) and the
    /// presentation block can never contradict the operations it accompanies.
    /// <para>
    /// The list endpoint (<see cref="GetProposalsAsync"/>) now maps through the same builder, fed by
    /// the batched <see cref="GetEffectiveRevisionsAsync"/>, so a review-queue card can no longer
    /// show the ORIGINAL summary/operations while the detail view, diff and Apply all use the revised
    /// set (#1444). The earlier original-operations boundary on list reads existed only to avoid a
    /// per-proposal revision query; the batch read removes that cost, so the boundary is gone rather
    /// than narrowed. List items continue to expose <see cref="ProposalDto.ApprovedRevisionId"/>.
    /// </para>
    /// </summary>
    private async Task<Result<ProposalDto>> BuildEffectiveProposalDtoAsync(
        AutomationProposal proposal,
        CancellationToken cancellationToken)
    {
        var effectiveRevision = await GetEffectiveRevisionAsync(proposal, cancellationToken);
        return Result.Success(BuildEffectiveProposalDto(proposal, effectiveRevision));
    }

    /// <summary>
    /// Synchronous core of <see cref="BuildEffectiveProposalDtoAsync"/>: maps the proposal to its
    /// response DTO, rebuilding <see cref="ProposalDto.Operations"/> and
    /// <see cref="ProposalDto.Presentation"/> together from the supplied EFFECTIVE revision (or the
    /// originals when it is null). Split out so callers that have ALREADY read the effective
    /// revision — <see cref="ApproveProposalAsync"/> passes the latest revision it read to pin as
    /// <see cref="AutomationProposal.ApprovedRevisionId"/> — can build the DTO without a redundant
    /// re-query (Gemini review, #1439), and the batched list read can build a whole page without a
    /// per-proposal query (#1444). Returns a bare DTO rather than a <see cref="Result{T}"/> because
    /// every branch succeeds: an unmaterializable revision degrades to the original operations, it does
    /// not fail the read.
    /// <para>
    /// Known unevenness (#1444 review): this overwrites <c>Operations</c> and <c>Presentation</c> but
    /// leaves the stored <see cref="ProposalDto.DiffPreview"/> alone, so a proposal that carried a
    /// creation-time preview could in principle expose an original-operations preview beside revised
    /// operations — the split-brain <see cref="GetTerminalProposalStoredPreviewAsync"/> suppresses on
    /// the MCP terminal path. Unreachable today: nothing in the backend calls
    /// <see cref="AutomationProposal.SetDiffPreview"/>, so <c>DiffPreview</c> is always null in
    /// practice. Deliberately NOT suppressed here — doing so would push more readers onto the
    /// recorded-operations fallback, which is exactly the surface #1464 is about.
    /// </para>
    /// </summary>
    private static ProposalDto BuildEffectiveProposalDto(
        AutomationProposal proposal,
        ProposalRevision? effectiveRevision)
    {
        var dto = MapToDto(proposal);

        if (effectiveRevision is null)
            return dto;

        if (!ProposalRevisionPayload.TryParseOperations(
                proposal.Id,
                effectiveRevision.RevisedPayload,
                out var revisedOperations,
                out _))
        {
            // A saved revision is structure-validated at creation, so an unparseable payload is a
            // defensive impossibility. A read must not fail on it — the diff/approve/apply gates
            // own surfacing that error — so fall back to the persisted original operations.
            return dto;
        }

        return dto with
        {
            Operations = revisedOperations,
            Presentation = BuildPresentation(
                proposal.Summary,
                proposal.RiskLevel,
                proposal.SourceType,
                revisedOperations)
        };
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

            // Echo the effective (latest-revision) operations the reviewer decided on, not the
            // stale originals, when the rejected proposal carried a saved revision (#1424).
            return await BuildEffectiveProposalDtoAsync(proposal, cancellationToken);
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

        // When a reviewer has saved a revision, Apply executes THAT payload — the executor
        // materializes the EFFECTIVE ProposalRevision (the pinned one once approved, the latest
        // while pending) via AutomationExecutorService.MaterializeEffectiveProposalAsync, not the
        // original operations. Build the diff from the same effective revision so the preview
        // equals what Apply will run (#1235) — and, once a proposal is approved, so a revision that
        // raced in after approval cannot make the diff diverge from the pinned apply set (#1428).
        // The stored DiffPreview is deliberately bypassed on this path because it describes the
        // original proposal, which is exactly the stale-preview bug we are fixing.
        var effectiveRevision = await GetEffectiveRevisionAsync(proposal, cancellationToken);
        if (effectiveRevision is not null)
        {
            if (!ProposalRevisionPayload.TryParseOperations(
                    id,
                    effectiveRevision.RevisedPayload,
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
            //
            // ONE deliberate difference from Apply: the Read bar (#1836). Reading the diff of a
            // proposal you authored is not a mutation, so it is gated on membership, while approve
            // and execute demand write-capable membership. The asymmetry only ever makes preview
            // MORE permissive than Apply, so it cannot resurrect the #1398 class (a clean preview
            // followed by a refused Apply is exactly what a Viewer-authored proposal SHOULD show:
            // the change is readable, and the refusal comes from the API-side #1794/#1827
            // CanWriteBoardAsync bar plus the Write bar below in approve/execute).
            var revisedValidation = await _policyEngine.ValidatePermissionsAsync(
                proposal.RequestedByUserId,
                proposal.BoardId,
                revisedOperations,
                BoardAccessBar.Read,
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
        // Apply's ValidatePolicy-then-ValidatePermissionsAsync order exactly — at the Read bar,
        // for the reason spelled out on the revision-aware branch above (#1836).
        var originalValidation = await _policyEngine.ValidatePermissionsAsync(
            proposal.RequestedByUserId,
            proposal.BoardId,
            originalOperations,
            BoardAccessBar.Read,
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

    public async Task<Result<string>> GetTerminalProposalStoredPreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
        if (proposal == null)
            return Result.Failure<string>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

        // A decided proposal's diff is historical: rebuilding it against the current board would
        // describe changes that already happened (or were rejected), so the STORED preview is
        // served rather than a live diff (#1397). But the requester/board-access half of the gate
        // must still hold — the shared AutomationPolicyEngine.ValidateBoardAccessAsync, the exact
        // checks (and codes/messages) ValidatePermissionsAsync composes on the live diff path
        // (#1398/#1413): requester exists → 404, board exists → 404, requester has board access
        // → 403. This closes the MCP preview==apply asymmetry (#1415) where a reviewer who lost
        // board access, or whose board was deleted, could still read the stored preview. The
        // operation-contract validator and the pre-decision structure/expiry gates are
        // intentionally NOT run: they no longer apply to a completed proposal, and re-validating
        // a historical preview against LIVE board state would wrongly deny it whenever a
        // referenced card/column/label was later deleted — or always, for an Applied create-card
        // whose TargetId now resolves. This path calls ValidateBoardAccessAsync directly (rather
        // than ValidatePermissionsAsync) precisely to skip that operation-contract validation;
        // both surface the identical access codes/messages for operation-less proposals now that
        // ValidatePermissionsAsync no longer short-circuits the board half on an empty list (#1426).
        //
        // The bar is Read (#1836). This is the read lane the write mirror must not capture: MCP
        // proposal_detail THROWS on a failed preview result (ProposalResources.GetProposalDetail),
        // so a write bar here would cost a board member demoted to Viewer the entire detail
        // resource for proposals they authored themselves — not merely the preview field.
        var accessValidation = await _policyEngine.ValidateBoardAccessAsync(
            proposal.RequestedByUserId,
            proposal.BoardId,
            BoardAccessBar.Read,
            cancellationToken);
        if (!accessValidation.IsSuccess)
            return Result.Failure<string>(accessValidation.ErrorCode, accessValidation.ErrorMessage);

        // Suppress the stored preview when an effective revision applies to this proposal (a pinned
        // ApprovedRevisionId, or the decision-time-frozen revision for a rejected one). The stored
        // DiffPreview is built from the proposal's ORIGINAL operations, so serving it next to a
        // revision-derived operation set would let a single MCP proposal_detail payload carry two
        // disagreeing views of the same change (Codex review, #1439). Returning null omits the
        // field, matching the never-stored shape. Production never persists DiffPreview today (the
        // V1 generator that wrote it was removed in #1214), so this is a consistency guard for
        // legacy/test data; a terminal proposal WITHOUT a revision still serves its stored preview
        // exactly as before (the #1397 decision is unchanged).
        var effectiveRevision = await GetEffectiveRevisionAsync(proposal, cancellationToken);
        if (effectiveRevision is not null)
            return Result.Success<string>(null!);

        // A never-stored preview passes through as null (never coerced to ""), so callers can
        // distinguish never-stored from stored-but-empty. Under the MCP resource serializer's
        // WhenWritingNull policy this omits the field — exactly how the raw DiffPreview field
        // serialized before the gating.
        return Result.Success(proposal.DiffPreview!);
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
        var operationDtos = proposal.Operations.Select(MapOperationToDto).ToList();

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
            operationDtos
        )
        {
            // Presentation is built from the SAME list assigned to Operations, so a ProposalDto
            // can never describe one operation set while presenting another (#1424 split-brain
            // guard). Effective-DTO callers rebuild both together from the revised set.
            Presentation = BuildPresentation(proposal.Summary, proposal.RiskLevel, proposal.SourceType, operationDtos),
            IsExpired = proposal.IsExpired,
            DeferredUntil = proposal.DeferredUntil,
            ApprovedRevisionId = proposal.ApprovedRevisionId
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

    /// <summary>
    /// Builds the human-readable presentation block from an explicit operation-DTO list rather
    /// than the entity's persisted operations, so callers rendering a revision's EFFECTIVE set
    /// (#1424) produce a presentation that matches the operations they return — the DTO can
    /// never present one operation set while carrying another.
    /// </summary>
    private static ProposalPresentationDto BuildPresentation(
        string summary,
        RiskLevel riskLevel,
        ProposalSourceType sourceType,
        IReadOnlyList<ProposalOperationDto> operations)
    {
        var orderedOperations = operations
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

        var isCaptureTaskBatch = IsCaptureTaskBatch(sourceType, orderedOperations);

        return new ProposalPresentationDto(
            BuildPlainSummary(summary, isCaptureTaskBatch, orderedOperations, affectedEntities),
            BuildImpactSummary(orderedOperations.Count, affectedEntities, isCaptureTaskBatch),
            BuildRiskCue(riskLevel),
            BuildSourceCue(sourceType),
            operationHeadlines,
            affectedEntities);
    }

    private static string BuildPlainSummary(
        string summary,
        bool isCaptureTaskBatch,
        IReadOnlyList<ProposalOperationDto> orderedOperations,
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

    private static string DescribeOperation(ProposalOperationDto operation)
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

        if (string.Equals(operation.TargetType, "column", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.ActionType, "create", StringComparison.OrdinalIgnoreCase))
        {
            var columnName = ExtractStringParameter(operation.Parameters, "name") ?? "(unspecified)";
            var position = ExtractInt32Parameter(operation.Parameters, "position");
            var wipLimit = ExtractInt32Parameter(operation.Parameters, "wipLimit");
            var wipLimitDescription = wipLimit.HasValue
                ? $"WIP limit {wipLimit.Value}"
                : "no WIP limit";

            return position.HasValue
                ? $"{operation.Sequence}. {verb} column \"{columnName}\" at position {position.Value}; {wipLimitDescription}"
                : $"{operation.Sequence}. {verb} column \"{columnName}\"; {wipLimitDescription}";
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

    private static bool IsCaptureTaskBatch(ProposalSourceType sourceType, IReadOnlyList<ProposalOperationDto> orderedOperations)
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
