using System.Globalization;
using System.Text;

namespace Taskdeck.Application.Services;

/// <summary>
/// Removes terminal control characters and invisible Unicode format characters from values that
/// will be written to logs.
/// </summary>
internal static class LogControlCharacterSanitizer
{
    public static string Strip(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder? sanitized = null;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (IsControlCharacter(character) || IsUnpairedSurrogate(value, index))
            {
                sanitized ??= new StringBuilder(value.Length).Append(value, 0, index);
                continue;
            }

            sanitized?.Append(character);
        }

        return sanitized?.ToString() ?? value;
    }

    public static string Truncate(string value, int maxLength, string suffix)
        => SurrogateSafeTruncation.Truncate(value, maxLength, suffix);

    private static bool IsUnpairedSurrogate(string value, int index)
    {
        var character = value[index];
        if (char.IsHighSurrogate(character))
        {
            return index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]);
        }

        return char.IsLowSurrogate(character) &&
            (index == 0 || !char.IsHighSurrogate(value[index - 1]));
    }

    // C0, DEL and C1 controls, the Unicode line and paragraph separators, and every format
    // character in the Basic Multilingual Plane (general category Cf; the check is per UTF-16 code
    // unit, so supplementary-plane format characters are not covered). Cf covers the zero-width
    // and bidirectional overrides
    // (U+200B..U+200F, U+202A..U+202E, U+2060..U+2064, U+FEFF, and the soft hyphen): they are
    // invisible in a log viewer and can reverse the rendered order of the text that follows, so a
    // caller-controlled value carrying them can make a log line read as something it is not.
    // Surrogates are category Cs, not Cf, so a valid surrogate pair is untouched here and unpaired
    // halves stay the business of IsUnpairedSurrogate.
    private static bool IsControlCharacter(char character) =>
        character <= '\u001F'
        || (character >= '\u007F' && character <= '\u009F')
        || character is '\u2028' or '\u2029'
        || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format;
}
