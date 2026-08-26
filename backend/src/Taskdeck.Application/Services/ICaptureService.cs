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

    Task<Result<CaptureItemDto>> KeepAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<Result<CaptureItemDto>> ArchiveAsync(
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

    /// <summary>
    /// Enqueues a capture for triage into an automation proposal. Requires write-capable membership
    /// (Owner/Admin/Editor, or board owner) on the board the proposal will target — whether that
    /// board is already linked to the capture or supplied here as <paramref name="targetBoardId"/>.
    /// Read-only members get <see cref="ErrorCodes.Forbidden"/> (#1794). Approval and execution
    /// authorization are unaffected.
    /// </summary>
    Task<Result<CaptureTriageEnqueueResultDto>> EnqueueTriageAsync(
        Guid userId,
        Guid itemId,
        Guid? targetBoardId = null,
        CancellationToken cancellationToken = default);

    Task<Result<BatchTriageResultDto>> BatchTriageAsync(
        Guid userId,
        BatchTriageRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<CaptureItemDto>> UpdateSuggestionAsync(
        Guid userId,
        Guid itemId,
        UpdateCaptureSuggestionDto dto,
        CancellationToken cancellationToken = default);
}
