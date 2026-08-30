using System.Globalization;

namespace Taskdeck.Application.Services;

internal static class LogValueSanitizer
{
    private const int MaxLength = 200;

    public static string Sanitize(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var sanitized = LogControlCharacterSanitizer.Strip(text);

        return sanitized.Length <= MaxLength
            ? sanitized
            : string.Concat(sanitized.AsSpan(0, MaxLength), "...");
    }
}
