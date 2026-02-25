using System;
using System.Linq;

namespace Taskdeck.Application.DTOs;

public record ApplyStarterPackDto(
    StarterPackManifestDto? Manifest,
    bool DryRun
);

public static class StarterPackConflictSeverity
{
    public const string Blocking = "blocking";
    public const string Warning = "warning";
}

public record StarterPackApplyActionDto(
    string EntityType,
    string Operation,
    string Key,
    string Reason
);

public record StarterPackApplyConflictDto(
    string Code,
    string Path,
    string Message,
    string? ExistingValue,
    string? IncomingValue,
    string Severity = StarterPackConflictSeverity.Blocking
);

public record StarterPackApplyResultDto(
    Guid BoardId,
    string PackId,
    bool DryRun,
    bool Applied,
    List<StarterPackApplyActionDto> Actions,
    List<StarterPackApplyConflictDto> Conflicts
)
{
    public bool HasConflicts => Conflicts.Count > 0;
    public bool HasBlockingConflicts => Conflicts.Any(conflict =>
    {
        // Treat missing severities as blocking for backward compatibility with older/external payload shapes.
        var severity = string.IsNullOrWhiteSpace(conflict.Severity)
            ? StarterPackConflictSeverity.Blocking
            : conflict.Severity;

        return string.Equals(severity, StarterPackConflictSeverity.Blocking, StringComparison.OrdinalIgnoreCase);
    });
}
