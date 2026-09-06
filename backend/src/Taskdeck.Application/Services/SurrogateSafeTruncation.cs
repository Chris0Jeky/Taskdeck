namespace Taskdeck.Application.Services;

/// <summary>
/// Truncates UTF-16 text without leaving a high surrogate separated from its low surrogate.
/// </summary>
internal static class SurrogateSafeTruncation
{
    public static string Truncate(string value, int maxLength, string suffix)
    {
        if (value.Length <= maxLength)
            return value;

        var length = maxLength;
        if (length > 0 &&
            char.IsHighSurrogate(value[length - 1]) &&
            length < value.Length &&
            char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        return string.Concat(value.AsSpan(0, length), suffix);
    }
}
