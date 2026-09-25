using System.Text;

namespace Taskdeck.Domain.Common;

/// <summary>
/// Centralized server-side password policy (#3402/#3419). Applied to every
/// user-supplied password entry point (register, change-password, admin create)
/// before hashing. Client-side checks remain UX-only.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>
    /// Minimum password length in characters. Matches the registration form's
    /// client-side rule so server and UX agree; raising it is a product decision.
    /// </summary>
    public const int MinLength = 6;

    /// <summary>
    /// Maximum password length in UTF-8 bytes. BCrypt only consumes the first
    /// 72 bytes, so longer inputs must be rejected rather than silently truncated.
    /// </summary>
    public const int MaxByteLength = 72;

    /// <summary>
    /// Returns null when the password satisfies the policy, otherwise a
    /// human-readable rejection reason. Passwords are evaluated verbatim;
    /// leading and trailing spaces are significant characters.
    /// </summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Password is required.";

        if (password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";

        if (string.IsNullOrWhiteSpace(password))
            return "Password must not be blank.";

        if (Encoding.UTF8.GetByteCount(password) > MaxByteLength)
            return $"Password must not exceed {MaxByteLength} bytes.";

        return null;
    }
}
