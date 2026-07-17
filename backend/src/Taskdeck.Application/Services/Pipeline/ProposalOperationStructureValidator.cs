using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Pipeline;

/// <summary>
/// Validates the structural invariants Apply enforces on a proposal's operation set:
/// operation count, unique non-negative sequences, and per-operation parameter size.
/// Extracted so preview and apply share one source of truth — the executor runs it via
/// <c>AutomationPolicyEngine.ValidateOperationStructure</c>, the revision-save path runs
/// it directly, and the original-proposal diff runs it before rendering (#1370 preview ==
/// apply). Deliberately dependency-free (pure shape checks) so it can be called anywhere
/// the effective operation set is materialized.
/// </summary>
public static class ProposalOperationStructureValidator
{
    public const int MaxOperationCount = 50;
    public const int MaxParametersLength = 10000;

    public static Result Validate(IReadOnlyCollection<ProposalOperationDto> operations)
    {
        if (operations == null || operations.Count == 0)
            return Result.Failure(ErrorCodes.ValidationError, "Proposal must contain at least one operation");

        if (operations.Count > MaxOperationCount)
            return Result.Failure(ErrorCodes.ValidationError, $"Proposal exceeds maximum operation count of {MaxOperationCount}");

        // Validate operation sequences are unique and non-negative
        var sequences = operations.Select(o => o.Sequence).ToList();
        if (sequences.Distinct().Count() != sequences.Count)
            return Result.Failure(ErrorCodes.ValidationError, "Operation sequences must be unique");

        if (sequences.Any(s => s < 0))
            return Result.Failure(ErrorCodes.ValidationError, "Operation sequences must be non-negative");

        // Validate parameters presence and size. The DTO declares Parameters non-nullable,
        // but legacy rows / nullable DB data can surface null at runtime; fail closed with
        // the same ValidationError on preview and apply (both share this validator) rather
        // than throw.
        foreach (var operation in operations)
        {
            if (operation.Parameters is null)
                return Result.Failure(ErrorCodes.ValidationError, "Operation parameters must be provided");

            if (operation.Parameters.Length > MaxParametersLength)
                return Result.Failure(ErrorCodes.ValidationError, $"Operation parameters exceed maximum length of {MaxParametersLength}");
        }

        return Result.Success();
    }
}
