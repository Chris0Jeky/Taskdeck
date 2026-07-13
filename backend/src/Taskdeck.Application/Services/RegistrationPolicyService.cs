using System.Security.Cryptography;
using System.Text;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class RegistrationPolicyService : IRegistrationPolicyService
{
    public const string RegistrationClosedMessage = "Registration is closed by this Taskdeck instance.";
    public const string InviteRequiredMessage = "A valid registration invite is required.";
    public static readonly TimeSpan MinimumInviteLifetime = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumInviteLifetime = TimeSpan.FromDays(365);

    private const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private readonly RegistrationSettings _settings;
    private readonly IRegistrationPolicyStore _store;
    private readonly IUnitOfWork _unitOfWork;

    public RegistrationPolicyService(
        RegistrationSettings settings,
        IRegistrationPolicyStore store,
        IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _store = store;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> CheckNewUserEligibilityAsync(
        string? inviteCode,
        CancellationToken cancellationToken = default)
    {
        if (_settings.Mode == RegistrationMode.Open)
            return Result.Success();

        if (_settings.Mode == RegistrationMode.Closed
            && await _store.IsFirstUserBootstrapClaimedAsync(cancellationToken))
        {
            return Result.Failure(ErrorCodes.Forbidden, RegistrationClosedMessage);
        }

        if (!IsWellFormedInviteCode(inviteCode))
            return GetRestrictiveFailure();

        var available = await _store.IsInviteAvailableAsync(
            HashInviteCode(inviteCode!),
            DateTimeOffset.UtcNow,
            cancellationToken);

        return available ? Result.Success() : GetRestrictiveFailure();
    }

    public async Task<Result<RegistrationAuthorization>> AuthorizeNewUserAsync(
        string? inviteCode,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // The singleton claim is written inside the caller's registration
        // transaction. Open mode needs no ceremony, but still records the claim
        // so a later switch to a restrictive mode cannot reopen bootstrap.
        // Restrictive modes require an operator-minted invite for the first user;
        // a failed invite or user write rolls both the claim and consumption back.
        var claimedBootstrap = await _store.TryClaimFirstUserBootstrapAsync(now, cancellationToken);

        if (_settings.Mode == RegistrationMode.Open)
            return Result.Success(new RegistrationAuthorization(claimedBootstrap));

        if (_settings.Mode == RegistrationMode.Closed && !claimedBootstrap)
            return GetRestrictiveAuthorizationFailure();

        if (!IsWellFormedInviteCode(inviteCode))
        {
            return GetRestrictiveAuthorizationFailure();
        }

        var consumed = await _store.TryConsumeInviteAsync(
            HashInviteCode(inviteCode!),
            now,
            cancellationToken);

        if (consumed)
            return Result.Success(new RegistrationAuthorization(claimedBootstrap));

        return GetRestrictiveAuthorizationFailure();
    }

    private Result<RegistrationAuthorization> GetRestrictiveAuthorizationFailure()
    {
        return Result.Failure<RegistrationAuthorization>(
            ErrorCodes.Forbidden,
            _settings.Mode == RegistrationMode.Closed
                ? RegistrationClosedMessage
                : InviteRequiredMessage);
    }

    public async Task<Result<RegistrationInviteResult>> CreateInviteAsync(
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        if (expiresIn < MinimumInviteLifetime || expiresIn > MaximumInviteLifetime)
        {
            return Result.Failure<RegistrationInviteResult>(
                ErrorCodes.ValidationError,
                "Registration invite lifetime must be between 1 minute and 365 days.");
        }

        var code = GenerateInviteCode();
        var expiresAt = DateTimeOffset.UtcNow.Add(expiresIn);
        var invite = new RegistrationInvite(
            HashInviteCode(code),
            code[..10],
            expiresAt);

        await _store.AddInviteAsync(invite, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RegistrationInviteResult(
            invite.Id,
            code,
            invite.DisplayPrefix,
            invite.CreatedAt,
            invite.ExpiresAt));
    }

    public static string GenerateInviteCode()
    {
        var builder = new StringBuilder(RegistrationInvite.CodePrefix, RegistrationInvite.RawCodeLength);
        for (var i = 0; i < RegistrationInvite.RandomPartLength; i++)
        {
            builder.Append(Base62Chars[RandomNumberGenerator.GetInt32(Base62Chars.Length)]);
        }

        return builder.ToString();
    }

    public static string HashInviteCode(string inviteCode)
    {
        var bytes = Encoding.UTF8.GetBytes(inviteCode);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool IsWellFormedInviteCode(string? inviteCode)
    {
        return inviteCode is { Length: RegistrationInvite.RawCodeLength }
            && inviteCode.StartsWith(RegistrationInvite.CodePrefix, StringComparison.Ordinal)
            && inviteCode[RegistrationInvite.CodePrefix.Length..]
                .All(character => Base62Chars.Contains(character));
    }

    private Result GetRestrictiveFailure()
    {
        return Result.Failure(
            ErrorCodes.Forbidden,
            _settings.Mode == RegistrationMode.Closed
                ? RegistrationClosedMessage
                : InviteRequiredMessage);
    }
}
