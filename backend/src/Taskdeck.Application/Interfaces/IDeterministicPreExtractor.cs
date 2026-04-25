namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Extracts deterministic structured data from free text using rule-based recognizers.
/// Covers dates, numbers, durations, URLs, and emails -- no LLM needed.
/// </summary>
public interface IDeterministicPreExtractor
{
    /// <summary>
    /// Extracts all recognized entities from the input text.
    /// </summary>
    /// <param name="text">Free-text input to analyze.</param>
    /// <param name="culture">BCP-47 culture code (default "en-us").</param>
    /// <returns>A list of extracted entities with their types, values, and positions.</returns>
    IReadOnlyList<ExtractedEntity> Extract(string text, string culture = "en-us");
}

/// <summary>
/// A single entity extracted from text by a deterministic recognizer.
/// </summary>
public sealed class ExtractedEntity
{
    /// <summary>
    /// The category of the extraction (e.g., "DateTime", "Number", "Duration", "Url", "Email").
    /// </summary>
    public string EntityType { get; }

    /// <summary>
    /// The raw text that was matched in the input.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The resolved/normalized value (e.g., ISO date string, numeric value).
    /// </summary>
    public string ResolvedValue { get; }

    /// <summary>
    /// Start offset in the source text.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// End offset in the source text.
    /// </summary>
    public int End { get; }

    public ExtractedEntity(string entityType, string text, string resolvedValue, int start, int end)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        ResolvedValue = resolvedValue ?? throw new ArgumentNullException(nameof(resolvedValue));
        Start = start;
        End = end;
    }

    public override string ToString() => $"[{EntityType}] \"{Text}\" -> \"{ResolvedValue}\" ({Start}..{End})";
}
