namespace Taskdeck.Application.Services;

/// <summary>
/// Builds the bounded, whitespace-normalized text shown in capture summaries and import results.
/// </summary>
internal static class CaptureTextExcerpt
{
    private const int DefaultMaxLength = 200;

    public static string Build(string text, int maxLength = DefaultMaxLength)
    {
        var normalized = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return SurrogateSafeTruncation.Truncate(normalized, maxLength, string.Empty);
    }
}
