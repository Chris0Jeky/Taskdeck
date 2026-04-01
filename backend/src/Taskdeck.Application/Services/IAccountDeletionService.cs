using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service for account deletion and anonymization following GDPR-style requirements.
/// Anonymizes user references in shared data, deletes personal data, and logs the action.
/// </summary>
public interface IAccountDeletionService
{
    /// <summary>
    /// Delete/anonymize the specified user's account. This is an irreversible operation.
    /// Requires password re-authentication and explicit confirmation phrase as safeguards.
    /// </summary>
    /// <param name="userId">The authenticated user's ID (from claims).</param>
    /// <param name="request">Contains current password and confirmation phrase.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with deletion summary or error.</returns>
    Task<Result<AccountDeletionResultDto>> DeleteAccountAsync(
        Guid userId,
        AccountDeletionRequest request,
        CancellationToken cancellationToken = default);
}
