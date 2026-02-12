using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class HistoryService : IHistoryService
{
    private const int MaxHistoryLimit = 1000;
    private readonly IUnitOfWork _unitOfWork;

    public HistoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IEnumerable<AuditLogDto>>> GetBoardHistoryAsync(Guid boardId, int limit = 100)
    {
        if (boardId == Guid.Empty)
            return Result.Failure<IEnumerable<AuditLogDto>>(ErrorCodes.ValidationError, "Board ID cannot be empty");

        if (!IsValidLimit(limit))
            return Result.Failure<IEnumerable<AuditLogDto>>(ErrorCodes.ValidationError, $"Limit must be between 1 and {MaxHistoryLimit}");

        var logs = await _unitOfWork.AuditLogs.GetByBoardAsync(boardId, limit);
        return Result.Success(logs.Select(MapToDto));
    }

    public async Task<Result<IEnumerable<AuditLogDto>>> GetEntityHistoryAsync(string entityType, Guid entityId, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return Result.Failure<IEnumerable<AuditLogDto>>(ErrorCodes.ValidationError, "Entity type cannot be empty");

        if (entityId == Guid.Empty)
            return Result.Failure<IEnumerable<AuditLogDto>>(ErrorCodes.ValidationError, "Entity ID cannot be empty");

        if (!IsValidLimit(limit))
            return Result.Failure<IEnumerable<AuditLogDto>>(ErrorCodes.ValidationError, $"Limit must be between 1 and {MaxHistoryLimit}");

        var logs = await _unitOfWork.AuditLogs.GetByEntityAsync(entityType, entityId, limit);
        return Result.Success(logs.Select(MapToDto));
    }

    public async Task<Result<IEnumerable<AuditLogDto>>> GetUserHistoryAsync(Guid userId, int limit = 100)
    {
        if (userId == Guid.Empty)
            return Result.Failure<IEnumerable<AuditLogDto>>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (!IsValidLimit(limit))
            return Result.Failure<IEnumerable<AuditLogDto>>(ErrorCodes.ValidationError, $"Limit must be between 1 and {MaxHistoryLimit}");

        var logs = await _unitOfWork.AuditLogs.GetByUserAsync(userId, limit);
        return Result.Success(logs.Select(MapToDto));
    }

    public async Task<Result> LogActionAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null)
    {
        try
        {
            var auditLog = new AuditLog(entityType, entityId, action, userId, changes);
            await _unitOfWork.AuditLogs.AddAsync(auditLog);
            await _unitOfWork.SaveChangesAsync();

            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }

    private static AuditLogDto MapToDto(AuditLog log)
    {
        return new AuditLogDto(
            log.Id,
            log.EntityType,
            log.EntityId,
            log.Action,
            log.UserId,
            log.User?.Username,
            log.Changes,
            log.Timestamp);
    }

    private static bool IsValidLimit(int limit) => limit is >= 1 and <= MaxHistoryLimit;
}
