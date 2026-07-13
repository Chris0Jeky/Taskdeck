using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public sealed record RegistrationInviteResult(
    Guid Id,
    string Code,
    string DisplayPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public interface IRegistrationPolicyService
{
    Task<Result> CheckNewUserEligibilityAsync(
        string? inviteCode,
        CancellationToken cancellationToken = default);

    Task<Result> AuthorizeNewUserAsync(
        string? inviteCode,
        CancellationToken cancellationToken = default);

    Task<Result<RegistrationInviteResult>> CreateInviteAsync(
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);
}
