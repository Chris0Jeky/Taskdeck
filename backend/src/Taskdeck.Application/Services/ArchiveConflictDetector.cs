using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Detects naming conflicts and resolves names during archive restore operations.
/// Pure logic — no I/O dependencies.
/// </summary>
public static class ArchiveConflictDetector
{
    /// <summary>
    /// Checks whether a name conflicts with existing names and resolves it
    /// according to the specified conflict strategy.
    /// </summary>
    /// <param name="originalName">The original name from the snapshot.</param>
    /// <param name="conflictExists">Whether a naming conflict was detected.</param>
    /// <param name="strategy">The conflict resolution strategy.</param>
    /// <param name="entityLabel">Label for error messages (e.g., "board", "column", "card").</param>
    /// <returns>A Result containing the resolved name, or a failure if strategy is Fail and conflict exists.</returns>
    public static Result<string> ResolveName(
        string originalName,
        bool conflictExists,
        ConflictStrategy strategy,
        string entityLabel)
    {
        if (!conflictExists)
            return Result.Success(originalName);

        return strategy switch
        {
            ConflictStrategy.Fail => Result.Failure<string>(
                ErrorCodes.Conflict,
                $"A {entityLabel} with name '{originalName}' already exists"),
            ConflictStrategy.Rename => Result.Success($"{originalName} (Restored)"),
            ConflictStrategy.AppendSuffix => Result.Success(
                $"{originalName} - {DateTime.UtcNow:yyyyMMdd-HHmmss}"),
            _ => Result.Failure<string>(
                ErrorCodes.ValidationError,
                $"Unknown conflict strategy: {strategy}")
        };
    }
}
