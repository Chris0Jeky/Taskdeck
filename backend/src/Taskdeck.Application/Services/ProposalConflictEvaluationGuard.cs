using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Fail-closed compatibility boundary for historical proposal rows whose operation
/// payloads cannot be evaluated by conflict review. The underlying detector remains
/// responsible for authorization and ordinary conflict discovery; this guard prevents
/// incomplete evaluation from being presented as affirmative safety evidence.
/// </summary>
public sealed class ProposalConflictEvaluationGuard : IProposalConflictDetector
{
    internal const string UnableToEvaluateKey = "unable-to-evaluate-operation";

    private readonly ProposalConflictDetector _inner;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProposalConflictEvaluationGuard> _logger;

    public ProposalConflictEvaluationGuard(
        ProposalConflictDetector inner,
        IUnitOfWork unitOfWork,
        ILogger<ProposalConflictEvaluationGuard> logger)
    {
        _inner = inner;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        ProposalDto effectiveProposal,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(effectiveProposal);

        var result = await _inner.DetectConflictsAsync(effectiveProposal, userId, cancellationToken);
        return ApplyIncompleteEvaluationGuard(
            result,
            effectiveProposal.Id,
            effectiveProposal.Operations);
    }

    public async Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.DetectConflictsAsync(proposalId, userId, cancellationToken);
        if (!result.IsSuccess)
            return result;

        // The id-based compatibility path is retained for existing application callers and
        // tests. The API review endpoint uses the effective ProposalDto overload, so revision
        // resolution remains server-authoritative and does not incur this second repository read.
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal is null)
            return result;

        var operations = proposal.Operations.Select(ToDto).ToList();
        return ApplyIncompleteEvaluationGuard(result, proposalId, operations);
    }

    private Result<IReadOnlyList<ConflictRowDto>> ApplyIncompleteEvaluationGuard(
        Result<IReadOnlyList<ConflictRowDto>> result,
        Guid proposalId,
        IReadOnlyList<ProposalOperationDto> operations)
    {
        if (!result.IsSuccess)
            return result;

        var unevaluatedOperationCount = operations.Count(IsUnevaluableConflictOperation);
        if (unevaluatedOperationCount == 0)
            return result;

        _logger.LogWarning(
            "Proposal conflict evaluation incomplete for {ProposalId}: unevaluated_operation_count={UnevaluatedOperationCount}",
            proposalId,
            unevaluatedOperationCount);

        // Any Ok row is affirmative evidence (status, capacity, or freshness). Retain
        // independent warnings/information, but do not let them coexist with positive claims
        // while even one relevant operation remains unevaluated.
        var guardedRows = result.Value
            .Where(row => row.Tone != ConflictTone.Ok &&
                          !row.Key.Equals(UnableToEvaluateKey, StringComparison.Ordinal))
            .ToList();

        guardedRows.Insert(0, new ConflictRowDto(
            ConflictTone.Warn,
            UnableToEvaluateKey,
            unevaluatedOperationCount == 1
                ? "1 proposal operation could not be evaluated"
                : $"{unevaluatedOperationCount} proposal operations could not be evaluated"));

        return Result.Success<IReadOnlyList<ConflictRowDto>>(guardedRows);
    }

    /// <summary>
    /// Mirrors the parameter-derived card occupancy seam currently evaluated by
    /// <see cref="ProposalConflictDetector"/>. It deliberately does not attempt a generic
    /// proposal-schema rewrite: only card operations whose destination/lifecycle projection
    /// feeds conflict review are classified here.
    /// </summary>
    private static bool IsUnevaluableConflictOperation(ProposalOperationDto operation)
    {
        if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
            return false;

        var action = operation.ActionType.Trim().ToLowerInvariant();
        if (action is not ("create" or "move" or "archive-lifecycle" or "restore-lifecycle" or "delete"))
            return false;

        if (!OperationParameterParser.TryDeserializeParameters(
                operation.Parameters,
                out var parameters,
                out _))
        {
            return true;
        }

        if (action is "create" or "move")
        {
            if (!TryGetDestinationColumnId(parameters, out _))
                return true;

            return action == "move" && !TryGetCardId(operation, parameters, out _);
        }

        return !TryGetCardId(operation, parameters, out _);
    }

    private static bool TryGetDestinationColumnId(JsonElement parameters, out Guid columnId)
    {
        if (TryGetGuid(parameters, "columnId", out columnId))
            return true;

        return TryGetGuid(parameters, "targetColumnId", out columnId);
    }

    private static bool TryGetCardId(
        ProposalOperationDto operation,
        JsonElement parameters,
        out Guid cardId)
    {
        if (TryGetGuid(parameters, "cardId", out cardId))
            return true;

        return Guid.TryParse(operation.TargetId, out cardId);
    }

    private static bool TryGetGuid(JsonElement parameters, string name, out Guid value)
    {
        value = Guid.Empty;
        return parameters.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               Guid.TryParse(property.GetString(), out value);
    }

    private static ProposalOperationDto ToDto(AutomationProposalOperation operation) => new(
        operation.Id,
        operation.ProposalId,
        operation.Sequence,
        operation.ActionType,
        operation.TargetType,
        operation.TargetId,
        operation.Parameters,
        operation.IdempotencyKey,
        operation.ExpectedVersion);
}
