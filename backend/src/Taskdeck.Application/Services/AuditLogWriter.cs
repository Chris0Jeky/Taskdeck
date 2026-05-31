using Microsoft.Extensions.Logging;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// Shared safe audit-log writer (issue #1134).
///
/// Audit/history writes are secondary to the mutation they describe, so a failure must never
/// crash the mutation — but it must not be swallowed silently either. This replaces the
/// copy-pasted empty-catch <c>SafeLogAsync</c> that lived in BoardService/CardService/
/// ColumnService/LabelService: a thrown exception OR a returned failed <c>Result</c> is now
/// logged at Warning, while the mutation still proceeds.
/// </summary>
internal static class AuditLogWriter
{
    public static async Task SafeLogAsync(
        IHistoryService? historyService,
        ILogger? logger,
        string entityType,
        Guid entityId,
        AuditAction action,
        Guid? userId = null,
        string? changes = null)
    {
        if (historyService is null)
            return;

        try
        {
            var result = await historyService.LogActionAsync(entityType, entityId, action, userId, changes);
            if (result is null)
            {
                // Defensive: a null Result shouldn't happen in production, but classify it as a
                // failed write rather than letting the catch below mislabel it as a throw/NRE.
                logger?.LogWarning(
                    "Audit log write returned a null result for {EntityType} {EntityId} action {Action}. Mutation continues.",
                    entityType, entityId, action);
            }
            else if (!result.IsSuccess)
            {
                logger?.LogWarning(
                    "Audit log write failed for {EntityType} {EntityId} action {Action}: {ErrorCode} {ErrorMessage}. Mutation continues.",
                    entityType, entityId, action, result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "Audit log write threw for {EntityType} {EntityId} action {Action}. Mutation continues.",
                entityType, entityId, action);
        }
    }
}
