using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public interface IArchiveRecoveryService
{
    Task<Result<ArchiveItemDto>> CreateArchiveItemAsync(
        CreateArchiveItemDto dto, 
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<ArchiveItemDto>>> GetArchiveItemsAsync(
        string? entityType = null, 
        Guid? boardId = null, 
        RestoreStatus? status = null,
        int limit = 100,
        Guid? actingUserId = null,
        CancellationToken cancellationToken = default);

    Task<Result<ArchiveItemDto>> GetArchiveItemByIdAsync(
        Guid id, 
        Guid? actingUserId = null,
        CancellationToken cancellationToken = default);

    Task<Result<ArchiveItemDto>> GetArchiveItemByEntityAsync(
        string entityType,
        Guid entityId,
        Guid? actingUserId = null,
        CancellationToken cancellationToken = default);

    Task<Result<RestoreResult>> RestoreArchiveItemAsync(
        Guid id, 
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken = default);
}
