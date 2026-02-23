using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface ICaptureService
{
    Task<Result<CaptureItemDto>> CreateAsync(
        Guid userId,
        CreateCaptureItemDto dto,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CaptureItemSummaryDto>>> ListAsync(
        Guid userId,
        CaptureListFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<Result<CaptureItemDto>> GetByIdAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<Result> IgnoreAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<Result> CancelAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<Result<CaptureTriageEnqueueResultDto>> EnqueueTriageAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);
}
