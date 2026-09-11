namespace Taskdeck.Application.Services;

internal static class CaptureTextComparison
{
    // Reconciliation compares line endings without rewriting the stored source.
    // Corrections normalize queue text to LF while retaining the submitted source text.
    internal static bool Equivalent(string? source, string? queueText)
        => string.Equals(
            source is null ? null : ArtefactTextNormalization.NormalizeLineEndings(source),
            queueText is null ? null : ArtefactTextNormalization.NormalizeLineEndings(queueText),
            StringComparison.Ordinal);
}
