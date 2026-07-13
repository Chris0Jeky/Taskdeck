namespace Taskdeck.Application.Services;

/// <summary>
/// Establishes the single persisted offset convention for extracted text:
/// UTF-16 string indexes with every line ending normalized to LF.
/// </summary>
public static class ArtefactTextNormalization
{
    public static string NormalizeLineEndings(string text)
        => text.Contains('\r')
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : text;

    public static string TruncateWithoutSplittingSurrogatePair(string text, int maxCharacters)
    {
        if (text.Length <= maxCharacters)
            return text;

        var length = maxCharacters;
        if (length > 0 &&
            char.IsHighSurrogate(text[length - 1]) &&
            length < text.Length &&
            char.IsLowSurrogate(text[length]))
        {
            length--;
        }

        return text[..length];
    }

    public static bool HasUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    return true;
                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }
}
