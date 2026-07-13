using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public sealed record RegistrationInviteResult(
    Guid Id,
    string Code,
    string DisplayPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record RegistrationAuthorization(
    bool ClaimedFirstUserBootstrap);

public sealed record RegistrationAvailability(
    RegistrationMode Mode,
    bool IsRegistrationAvailable,
    bool InviteRequired);

public interface IRegistrationPolicyService
{
    Task<Result> CheckNewUserEligibilityAsync(
        string? inviteCode,
        CancellationToken cancellationToken = default);

    Task<Result<RegistrationAuthorization>> AuthorizeNewUserAsync(
        string? inviteCode,
        CancellationToken cancellationToken = default);

    Task<RegistrationAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<Result<RegistrationInviteResult>> CreateInviteAsync(
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);
}
