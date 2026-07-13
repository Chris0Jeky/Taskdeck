using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IRegistrationPolicyStore
{
    Task<bool> IsFirstUserBootstrapClaimedAsync(
        CancellationToken cancellationToken = default);

    Task<bool> IsInviteAvailableAsync(
        string codeHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> TryClaimFirstUserBootstrapAsync(
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken = default);

    Task<bool> TryConsumeInviteAsync(
        string codeHash,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken = default);

    Task AddInviteAsync(
        RegistrationInvite invite,
        CancellationToken cancellationToken = default);
}
