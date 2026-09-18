namespace Taskdeck.Application.Services;

internal static class CaptureTextComparison
{
    // Reconciliation compares line endings without rewriting the stored source.
    // Linked-transcript corrections normalize the queue projection to LF while retaining the
    // submitted source text; ordinary suggestion edits preserve the submitted bytes.
    internal static bool Equivalent(string? source, string? queueText)
        => string.Equals(
            source is null ? null : ArtefactTextNormalization.NormalizeLineEndings(source),
            queueText is null ? null : ArtefactTextNormalization.NormalizeLineEndings(queueText),
            StringComparison.Ordinal);
}
