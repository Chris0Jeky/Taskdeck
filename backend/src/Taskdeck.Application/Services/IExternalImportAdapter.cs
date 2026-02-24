using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IExternalImportAdapter
{
    string Provider { get; }

    Result<ExternalImportParseResult> Parse(ExternalImportRequestDto request);
}

public sealed record ExternalImportCandidate(
    int SourceRowNumber,
    string DedupeKey,
    string Title,
    string Description);

public sealed record ExternalImportParseResult(
    string Provider,
    string Profile,
    int RowsReceived,
    int RowsParsed,
    List<ExternalImportCandidate> Candidates,
    List<ExternalImportConflictDto> Conflicts);
