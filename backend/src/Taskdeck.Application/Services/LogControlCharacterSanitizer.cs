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
            if (IsControlCharacter(character))
            {
                sanitized ??= new StringBuilder(value.Length).Append(value, 0, index);
                continue;
            }

            sanitized?.Append(character);
        }

        return sanitized?.ToString() ?? value;
    }

    private static bool IsControlCharacter(char character) =>
        character <= '\u001F' || (character >= '\u007F' && character <= '\u009F');
}
