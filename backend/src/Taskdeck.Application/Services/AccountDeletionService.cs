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

    public AccountDeletionService(IUnitOfWork unitOfWork, IHistoryService historyService)
    {
        _unitOfWork = unitOfWork;
        _historyService = historyService;
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

        // Re-authenticate: verify current password
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Failure<AccountDeletionResultDto>(ErrorCodes.AuthenticationFailed, "Invalid password");

        try
        {
            // Log the deletion request before modifying data (no PII in the log entry)
            await _historyService.LogActionAsync(
                "User", userId, AuditAction.AccountDeletionRequested, userId,
                "Account deletion requested by user");

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            // 1. Count audit logs linked to the user.
            //    AuditLog.UserId is immutable by domain design — these remain linked to
            //    the deactivated user record. The user's PII (username, email) is scrubbed
            //    below (step 7), so the FK reference resolves to an anonymized placeholder.
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

            // 4. Anonymize chat sessions — delete messages but retain session metadata
            //    without user linkage for aggregate analytics
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

            // 7. Anonymize and deactivate the user account (keeps the record for
            //    referential integrity but scrubs PII and marks as inactive).
            //    Audit logs still reference this user ID but resolve to anonymized fields.
            var anonymizedSuffix = userId.ToString("N")[..8];
            user.UpdateProfile(
                username: $"deleted-{anonymizedSuffix}",
                email: $"deleted-{anonymizedSuffix}@anonymized.local");
            user.UpdatePassword(BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()));
            user.Deactivate();
            await _unitOfWork.Users.UpdateAsync(user, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            // Log completion (no PII)
            await _historyService.LogActionAsync(
                "User", userId, AuditAction.AccountAnonymized, null,
                "Account anonymization completed");

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
            try
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            }
            catch
            {
                // Rollback failure is secondary — report the primary error
            }

            return Result.Failure<AccountDeletionResultDto>(
                ErrorCodes.UnexpectedError,
                $"Account deletion failed: {ex.Message}");
        }
    }
}
