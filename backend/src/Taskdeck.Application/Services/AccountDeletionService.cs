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
    private readonly ILogger<AccountDeletionService>? _logger;
    private readonly ISourceArtefactRepository _artefacts;
    private readonly ITranscriptRepository _transcripts;
    private readonly IWorkspaceInsightRepository _workspaceInsights;

    public AccountDeletionService(
        IUnitOfWork unitOfWork,
        IHistoryService historyService,
        ISourceArtefactRepository artefacts,
        ITranscriptRepository transcripts,
        IWorkspaceInsightRepository workspaceInsights,
        ILogger<AccountDeletionService>? logger = null,
        ICaptureStore? captureStore = null,
        IBlobStore? blobStore = null,
        IAudioTranscriptionStore? audioTranscription = null,
        CardAssignmentService? assignments = null,
        ICardAssignmentStore? assignmentStore = null)
    {
        _unitOfWork = unitOfWork;
        _historyService = historyService;
        _logger = logger;
        _artefacts = artefacts;
        _transcripts = transcripts;
        _workspaceInsights = workspaceInsights;
        _captureStore = captureStore;
        _blobStore = blobStore;
        _audioTranscription = audioTranscription;
        _assignments = assignments;
        _assignmentStore = assignmentStore;
    }

    private readonly ICaptureStore? _captureStore;
    private readonly IBlobStore? _blobStore;
    private readonly IAudioTranscriptionStore? _audioTranscription;
    private readonly CardAssignmentService? _assignments;
    private readonly ICardAssignmentStore? _assignmentStore;

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

        // Guard: board creation sets only Board.OwnerId (no Owner access row is ever
        // created), so also refuse deletion while the user owns active boards
        // outright - otherwise those boards would be orphaned permanently
        // (#3400, #3425). Archived boards are already disposed of (DELETE is a
        // soft delete and no ownership-transfer flow exists yet, see #3424),
        // so they do not block deletion.
        var ownedBoards = (await _unitOfWork.Boards.GetByOwnerIdAsync(userId, includeArchived: false, cancellationToken)).ToList();
        if (ownedBoards.Count > 0)
        {
            var firstOwned = ownedBoards.First();
            return Result.Failure<AccountDeletionResultDto>(
                ErrorCodes.InvalidOperation,
                $"Cannot delete account: you own board '{firstOwned.Name}' ({firstOwned.Id}). Delete the boards you own before deleting your account.");
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            if (_assignmentStore is not null)
                await _assignmentStore.RefreshAuthorityAsync(Guid.Empty, userId, cancellationToken);
            if (!user.IsActive)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountDeletionResultDto>(ErrorCodes.InvalidOperation, "Account is already deactivated");
            }
            var detachedAssignments = _assignments is null ? Array.Empty<Card>() :
                await _assignments.StageDetachAsync(userId, null, userId, "account-erased", cancellationToken);

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

            // 2. Delete notifications (personal data) — batched SQL DELETE avoids
            //    unbounded memory from fetching all rows and N+1 single-row deletes.
            var notificationsDeleted = await _unitOfWork.Notifications.DeleteByUserIdAsync(userId, cancellationToken);

            // 3. Delete capture/inbox items (personal data)
            var captures = await _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken);
            var captureItemsDeleted = 0;
            foreach (var capture in captures)
            {
                await _unitOfWork.LlmQueue.DeleteAsync(capture, cancellationToken);
                captureItemsDeleted++;
            }

            // 3b. Delete the durable Capture mirrors (ADR-0065). Rows exist only when
            //     ContextFabric:DualWriteCaptures was ever on; they carry user-authored titles and
            //     their FK to User is Restrict, so they must go inside this same transaction.
            if (_audioTranscription is not null)
                await _audioTranscription.DeleteOwnerAsync(userId, cancellationToken);
            var durableCapturesDeleted = _captureStore is null
                ? 0
                : await _captureStore.DeleteByUserAsync(userId, cancellationToken);
            if (_blobStore is not null)
                await _blobStore.DeleteOwnerAsync(userId, cancellationToken);

            // Artefact blobs are personal data. The repository performs set-based
            // deletion of blobs followed by metadata inside this account transaction.
            var artefactsDeleted = await _artefacts.DeleteByUserIdAsync(userId, cancellationToken);
            // Transcript evidence links are database-owned by their Transcript FK, so this
            // set-based delete cascades without a racy string-source-ID scan.
            var transcriptsDeleted = await _transcripts.DeleteByUserIdAsync(userId, cancellationToken);
            var privateWorkspaceDeleted = await _workspaceInsights.DeleteByUserAsync(userId, cancellationToken);

            // 4. Anonymize chat sessions with one set-based delete: the old per-session/
            // per-message loop issued 1+N queries plus a tracked delete per row, and silently
            // kept every session past the 100k fetch cap. Messages need no separate delete:
            // ChatMessage.SessionId is a required FK with DeleteBehavior.Cascade, so the
            // database removes them atomically with their sessions (same reliance as the
            // transcript-evidence cascade above). The receipt carries the exact session count.
            var chatSessionsAnonymized = await _unitOfWork.ChatSessions.DeleteByUserIdAsync(userId, cancellationToken);

            // 5. Delete external logins, MFA credentials, and API keys (authentication
            //    material must not outlive the account).
            var externalLogins = await _unitOfWork.ExternalLogins.GetByUserIdAsync(userId, cancellationToken);
            var externalLoginsDeleted = 0;
            foreach (var login in externalLogins)
            {
                await _unitOfWork.ExternalLogins.DeleteAsync(login, cancellationToken);
                externalLoginsDeleted++;
            }

            var mfaCredentialsDeleted = 0;
            var mfaCredential = await _unitOfWork.MfaCredentials.GetByUserIdAsync(userId, cancellationToken);
            if (mfaCredential is not null)
            {
                await _unitOfWork.MfaCredentials.DeleteByUserIdAsync(userId, cancellationToken);
                mfaCredentialsDeleted = 1;
            }

            var apiKeys = await _unitOfWork.ApiKeys.GetByUserIdAsync(userId, cancellationToken);
            var apiKeysDeleted = 0;
            foreach (var apiKey in apiKeys)
            {
                await _unitOfWork.ApiKeys.DeleteAsync(apiKey, cancellationToken);
                apiKeysDeleted++;
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
            // Credentials are deleted in step 5; also clear the flag so the anonymized
            // record is not MFA-marked (#3425).
            user.DisableMfa();
            user.Deactivate();
            await _unitOfWork.Users.UpdateAsync(user, cancellationToken);

            // Log completion inside the transaction (no PII)
            await _historyService.LogActionAsync(
                "User", userId, AuditAction.AccountAnonymized, null,
                "Account anonymization completed");

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            if (_assignments is not null)
                foreach (var card in detachedAssignments)
                    await _assignments.NotifyAsync(card.BoardId, card.Id, cancellationToken);

            return Result.Success(new AccountDeletionResultDto(
                Success: true,
                Message: "Account has been deleted and personal data anonymized",
                AuditLogsAnonymized: auditLogsAnonymized,
                NotificationsDeleted: notificationsDeleted,
                CaptureItemsDeleted: captureItemsDeleted,
                ChatSessionsAnonymized: chatSessionsAnonymized,
                ExternalLoginsDeleted: externalLoginsDeleted,
                PreferencesDeleted: preferencesDeleted,
                ArtefactsDeleted: artefactsDeleted,
                TranscriptsDeleted: transcriptsDeleted,
                DurableCapturesDeleted: durableCapturesDeleted,
                WorkspaceMemoriesDeleted: privateWorkspaceDeleted.Memories,
                WorkspaceMemoryRevisionsDeleted: privateWorkspaceDeleted.Revisions,
                QuietInsightsDeleted: privateWorkspaceDeleted.Insights,
                CardAssignmentsRemoved: detachedAssignments.Count,
                MfaCredentialsDeleted: mfaCredentialsDeleted,
                ApiKeysDeleted: apiKeysDeleted));
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
