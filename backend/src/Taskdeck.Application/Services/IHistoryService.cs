using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service interface for audit log and history operations.
/// SCAFFOLDING: Implementation pending.
/// </summary>
public interface IHistoryService
{
    Task<Result<IEnumerable<AuditLogDto>>> GetBoardHistoryAsync(Guid boardId, int limit = 100);
    Task<Result<IEnumerable<AuditLogDto>>> GetEntityHistoryAsync(string entityType, Guid entityId, int limit = 100);
    Task<Result<IEnumerable<AuditLogDto>>> GetUserHistoryAsync(Guid userId, int limit = 100);
    Task<Result> LogActionAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null);
}
