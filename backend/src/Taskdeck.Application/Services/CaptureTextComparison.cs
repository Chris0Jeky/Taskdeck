namespace Taskdeck.Application.Services;

internal static class CaptureTextComparison
{
    // Queue text uses LF; source assets retain the exact submitted line endings.
    internal static bool Equivalent(string? source, string? queueText)
        => string.Equals(
            source is null ? null : ArtefactTextNormalization.NormalizeLineEndings(source),
            queueText is null ? null : ArtefactTextNormalization.NormalizeLineEndings(queueText),
            StringComparison.Ordinal);
}
