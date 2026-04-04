using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Handles account deletion/anonymization following GDPR-style requirements.
/// Strategy:
/// - Refuse deletion if user is sole owner of any board (must transfer ownership first)
/// - Anonymize user references in shared data (audit logs, board accesses)
/// - Delete personal data (profile, external logins, preferences, notifications, captures)
/// - Deactivate the user account
/// - Log the deletion request (without PII) in the audit trail
/// </summary>
public class AccountDeletionService : IAccountDeletionService
{
    /// <summary>
    /// The exact phrase users must type to confirm account deletion.
    /// </summary>
    public const string RequiredConfirmationPhrase = "DELETE MY ACCOUNT";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IHistoryService _historyService;
    private readonly IActiveUserCache? _activeUserCache;
    private readonly ILogger<AccountDeletionService>? _logger;

    public AccountDeletionService(
        IUnitOfWork unitOfWork,
        IHistoryService historyService,
        IActiveUserCache? activeUserCache = null,
        ILogger<AccountDeletionService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _historyService = historyService;
        _activeUserCache = activeUserCache;
        _logger = logger;
    }

    public async Task<Result<AccountDeletionResultDto>> DeleteAccountAsync(
        Guid userId,
        AccountDeletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<AccountDeletionResultDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return Result.Failure<AccountDeletionResultDto>(ErrorCodes.ValidationError, "Current password is required for account deletion");

        if (!string.Equals(request.ConfirmationPhrase, RequiredConfirmationPhrase, StringComparison.Ordinal))
            return Result.Failure<AccountDeletionResultDto>(
                ErrorCodes.ValidationError,
                $"Confirmation phrase must be exactly: {RequiredConfirmationPhrase}");

        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure<AccountDeletionResultDto>(ErrorCodes.NotFound, "User not found");

        // Guard: refuse deletion for already-deactivated accounts (concurrency safety)
        if (!user.IsActive)
            return Result.Failure<AccountDeletionResultDto>(ErrorCodes.InvalidOperation, "Account is already deactivated");

        // Re-authenticate: verify current password
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Failure<AccountDeletionResultDto>(ErrorCodes.AuthenticationFailed, "Invalid password");

        // Guard: refuse deletion if user is sole owner of any board (must transfer ownership first)
        var boardAccesses = await _unitOfWork.BoardAccesses.GetByUserIdAsync(userId, cancellationToken);
        var ownerAccesses = boardAccesses.Where(ba => ba.Role == UserRole.Owner).ToList();
        foreach (var ownerAccess in ownerAccesses)
        {
            var allBoardMembers = await _unitOfWork.BoardAccesses.GetByBoardIdAsync(ownerAccess.BoardId, cancellationToken);
            var otherOwners = allBoardMembers.Where(ba => ba.UserId != userId && ba.Role == UserRole.Owner);
            if (!otherOwners.Any())
            {
                return Result.Failure<AccountDeletionResultDto>(
                    ErrorCodes.InvalidOperation,
                    $"Cannot delete account: you are the sole owner of board {ownerAccess.BoardId}. Transfer ownership first.");
            }
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            // Log the deletion request inside the transaction so it rolls back if deletion fails
            await _historyService.LogActionAsync(
                "User", userId, AuditAction.AccountDeletionRequested, userId,
                "Account deletion requested by user");

            // 1. Count audit logs linked to the user.
            //    AuditLog.UserId is immutable by domain design — these remain linked to
            //    the deactivated user record. The user's PII (username, email) is scrubbed
            //    below (step 8), so the FK reference resolves to an anonymized placeholder.
            var auditLogs = await _unitOfWork.AuditLogs.GetByUserAsync(userId, limit: 100000, cancellationToken: cancellationToken);
            var auditLogsAnonymized = auditLogs.Count();

            // 2. Delete notifications (personal data)
            var notifications = await _unitOfWork.Notifications.GetByUserIdAsync(userId, limit: 100000, cancellationToken: cancellationToken);
            var notificationsDeleted = 0;
            foreach (var notification in notifications)
            {
                await _unitOfWork.Notifications.DeleteAsync(notification, cancellationToken);
                notificationsDeleted++;
            }

            // 3. Delete capture/inbox items (personal data)
            var captures = await _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken);
            var captureItemsDeleted = 0;
            foreach (var capture in captures)
            {
                await _unitOfWork.LlmQueue.DeleteAsync(capture, cancellationToken);
                captureItemsDeleted++;
            }

            // 4. Anonymize chat sessions — delete messages and sessions
            var chatSessions = await _unitOfWork.ChatSessions.GetByUserIdAsync(userId, limit: 100000, cancellationToken: cancellationToken);
            var chatSessionsAnonymized = 0;
            foreach (var session in chatSessions)
            {
                var messages = await _unitOfWork.ChatMessages.GetBySessionIdAsync(session.Id, limit: 100000, cancellationToken: cancellationToken);
                foreach (var message in messages)
                {
                    await _unitOfWork.ChatMessages.DeleteAsync(message, cancellationToken);
                }
                await _unitOfWork.ChatSessions.DeleteAsync(session, cancellationToken);
                chatSessionsAnonymized++;
            }

            // 5. Delete external logins (personal data)
            var externalLogins = await _unitOfWork.ExternalLogins.GetByUserIdAsync(userId, cancellationToken);
            var externalLoginsDeleted = 0;
            foreach (var login in externalLogins)
            {
                await _unitOfWork.ExternalLogins.DeleteAsync(login, cancellationToken);
                externalLoginsDeleted++;
            }

            // 6. Delete user preferences (personal data)
            var preferencesDeleted = 0;
            var userPreference = await _unitOfWork.UserPreferences.GetByUserIdAsync(userId, cancellationToken);
            if (userPreference is not null)
            {
                await _unitOfWork.UserPreferences.DeleteAsync(userPreference, cancellationToken);
                preferencesDeleted++;
            }

            var notificationPreference = await _unitOfWork.NotificationPreferences.GetByUserIdAsync(userId, cancellationToken);
            if (notificationPreference is not null)
            {
                await _unitOfWork.NotificationPreferences.DeleteAsync(notificationPreference, cancellationToken);
                preferencesDeleted++;
            }

            // 7. Delete board access records (removes user-board linkage)
            foreach (var access in boardAccesses)
            {
                await _unitOfWork.BoardAccesses.DeleteAsync(access, cancellationToken);
            }

            // 8. Anonymize and deactivate the user account (keeps the record for
            //    referential integrity but scrubs PII and marks as inactive).
            //    Audit logs still reference this user ID but resolve to anonymized fields.
            //    Use a random suffix so the pseudonym cannot be reversed from the user ID.
            var anonymizedSuffix = Guid.NewGuid().ToString("N")[..12];
            user.UpdateProfile(
                username: $"deleted-{anonymizedSuffix}",
                email: $"deleted-{anonymizedSuffix}@anonymized.local");
            user.UpdatePassword(BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()));
            // Invalidate all active JWT tokens so that any in-flight sessions are
            // rejected by the TokenValidationMiddleware.
            user.InvalidateTokens();
            user.Deactivate();
            await _unitOfWork.Users.UpdateAsync(user, cancellationToken);

            // Log completion inside the transaction (no PII)
            await _historyService.LogActionAsync(
                "User", userId, AuditAction.AccountAnonymized, null,
                "Account anonymization completed");

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            // Invalidate the active-user cache AFTER the transaction commits so that
            // concurrent requests cannot repopulate the cache from the still-active row
            // during the commit window.
            _activeUserCache?.Invalidate(userId);

            return Result.Success(new AccountDeletionResultDto(
                Success: true,
                Message: "Account has been deleted and personal data anonymized",
                AuditLogsAnonymized: auditLogsAnonymized,
                NotificationsDeleted: notificationsDeleted,
                CaptureItemsDeleted: captureItemsDeleted,
                ChatSessionsAnonymized: chatSessionsAnonymized,
                ExternalLoginsDeleted: externalLoginsDeleted,
                PreferencesDeleted: preferencesDeleted));
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Account deletion failed for user {UserId}", userId);
            }

            try
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                _logger?.LogError(rollbackEx, "Transaction rollback also failed for user {UserId} account deletion", userId);
            }

            return Result.Failure<AccountDeletionResultDto>(
                ErrorCodes.UnexpectedError,
                "Account deletion failed due to an internal error");
        }
    }
}
