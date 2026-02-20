namespace Taskdeck.Application.DTOs;

public record ApplyStarterPackDto(
    StarterPackManifestDto? Manifest,
    bool DryRun
);

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
    string? IncomingValue
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
}
