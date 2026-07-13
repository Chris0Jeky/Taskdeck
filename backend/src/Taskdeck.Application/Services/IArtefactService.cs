using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public sealed record CreateArtefactRequest(
    Stream Content,
    string FileName,
    string MimeType,
    Guid? BoardId = null,
    Guid? CreatedFromCaptureId = null);

public interface IArtefactService
{
    Task<Result<SourceArtefactDto>> CreateAsync(
        Guid userId,
        CreateArtefactRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SourceArtefactDto>> GetMetadataAsync(
        Guid userId,
        Guid artefactId,
        CancellationToken cancellationToken = default);

    Task<Result> CopyContentAsync(
        Guid userId,
        Guid artefactId,
        Stream destination,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        Guid userId,
        Guid artefactId,
        CancellationToken cancellationToken = default);
}
