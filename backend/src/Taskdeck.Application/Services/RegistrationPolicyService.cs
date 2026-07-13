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

    public async Task<Result> AuthorizeNewUserAsync(
        string? inviteCode,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // The singleton claim is written inside the caller's registration
        // transaction. Exactly one fresh-install registration can bypass a
        // restrictive mode, and a failed user creation rolls the claim back.
        if (await _store.TryClaimFirstUserBootstrapAsync(now, cancellationToken))
            return Result.Success();

        if (_settings.Mode == RegistrationMode.Open)
            return Result.Success();

        if (_settings.Mode == RegistrationMode.Closed)
            return Result.Failure(ErrorCodes.Forbidden, RegistrationClosedMessage);

        if (!IsWellFormedInviteCode(inviteCode))
            return Result.Failure(ErrorCodes.Forbidden, InviteRequiredMessage);

        var consumed = await _store.TryConsumeInviteAsync(
            HashInviteCode(inviteCode!),
            now,
            cancellationToken);

        return consumed
            ? Result.Success()
            : Result.Failure(ErrorCodes.Forbidden, InviteRequiredMessage);
    }

    public async Task<Result<RegistrationInviteResult>> CreateInviteAsync(
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        if (expiresIn <= TimeSpan.Zero || expiresIn > MaximumInviteLifetime)
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
}
