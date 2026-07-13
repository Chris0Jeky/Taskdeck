using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IRegistrationPolicyStore
{
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
