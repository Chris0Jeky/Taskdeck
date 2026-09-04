using System.Text;

namespace Taskdeck.Application.Services;

/// <summary>
/// Removes terminal control characters from values that will be written to logs.
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

    private static bool IsControlCharacter(char character) =>
        character <= '\u001F'
        || (character >= '\u007F' && character <= '\u009F')
        || character is '\u2028' or '\u2029';
}
