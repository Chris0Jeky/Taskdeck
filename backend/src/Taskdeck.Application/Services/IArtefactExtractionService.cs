using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IArtefactExtractionService
{
    Task<Result<ArtefactExtractionDto>> ExtractAsync(
        Guid userId,
        Guid sourceArtefactId,
        CancellationToken cancellationToken = default);

    Task<Result<ArtefactExtractionDto>> GetLatestAsync(
        Guid userId,
        Guid sourceArtefactId,
        CancellationToken cancellationToken = default);
}
