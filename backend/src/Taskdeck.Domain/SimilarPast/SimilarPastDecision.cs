namespace Taskdeck.Domain.SimilarPast;

/// <summary>
/// A past proposal decision surfaced as context for the current proposal review.
/// Immutable value object.
/// </summary>
/// <param name="Serial">Display serial, e.g. '#001'.</param>
/// <param name="Title">Proposal summary or first operation description.</param>
/// <param name="Verdict">Whether the proposal was applied or rejected.</param>
/// <param name="Date">Pre-formatted date string, e.g. 'wk 14'.</param>
public sealed record SimilarPastDecision(
    string Serial,
    string Title,
    PastVerdict Verdict,
    string Date)
{
    /// <summary>
    /// Maximum title length to prevent unbounded strings from leaking into the UI.
    /// </summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Creates a <see cref="SimilarPastDecision"/> with validation.
    /// </summary>
    public static SimilarPastDecision Create(string serial, string title, PastVerdict verdict, string date)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("Serial cannot be empty.", nameof(serial));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));
        if (string.IsNullOrWhiteSpace(date))
            throw new ArgumentException("Date cannot be empty.", nameof(date));

        var truncatedTitle = title.Length > MaxTitleLength
            ? title[..MaxTitleLength]
            : title;

        return new SimilarPastDecision(serial, truncatedTitle, verdict, date);
    }
}
