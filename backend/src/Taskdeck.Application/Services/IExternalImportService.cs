using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IExternalImportService
{
    Task<Result<ExternalImportResultDto>> ImportToBoardAsync(
        Guid boardId,
        ExternalImportRequestDto request,
        CancellationToken cancellationToken = default);
}
