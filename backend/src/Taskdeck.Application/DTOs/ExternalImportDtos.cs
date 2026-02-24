namespace Taskdeck.Application.DTOs;

public static class ExternalImportProviders
{
    public const string Csv = "csv";
}

public static class ExternalImportProfiles
{
    public const string OutreachContactsV1 = "outreach.contacts.v1";
}

public static class ExternalImportMetadata
{
    public const string CardDescriptionPrefix = "[taskdeck-import-meta] ";
}

public sealed record ExternalImportRequestDto(
    string Provider,
    string Payload,
    string TargetColumnName,
    bool DryRun,
    string? Profile = null,
    ExternalImportCsvOptionsDto? Csv = null);

public sealed record ExternalImportCsvOptionsDto(
    string? DisplayNameColumn = null,
    string? FirstNameColumn = null,
    string? LastNameColumn = null,
    string? CompanyColumn = null,
    string? RoleColumn = null,
    string? EmailColumn = null,
    string? LinkedInUrlColumn = null,
    string? LastTouchAtColumn = null);

public sealed record ExternalImportConflictDto(
    string Code,
    string Path,
    string Message,
    string? ExistingValue = null,
    string? IncomingValue = null);

public sealed record ExternalImportResultDto(
    Guid BoardId,
    string Provider,
    string Profile,
    string TargetColumnName,
    bool DryRun,
    bool Applied,
    int RowsReceived,
    int RowsParsed,
    int RowsCreated,
    int RowsUpdated,
    int RowsSkipped,
    List<ExternalImportConflictDto> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}
