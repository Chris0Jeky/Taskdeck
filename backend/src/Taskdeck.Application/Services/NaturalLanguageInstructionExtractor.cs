using System.Text.RegularExpressions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Bridges the gap between intent classification (which detects that a message is
/// actionable) and instruction parsing (which requires structured syntax).
///
/// When the LLM classifier detects actionable intent but the raw message is natural
/// language, this extractor attempts to produce structured instructions that the
/// <see cref="AutomationPlannerService"/> parser can consume.
///
/// Used by MockLlmProvider and as a fallback for real providers when LLM-based
/// structured extraction fails.
/// </summary>
public static class NaturalLanguageInstructionExtractor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    // Patterns to extract a quoted title from user input
    private static readonly Regex QuotedTitlePattern = new(
        @"['""]([^'""]+)['""]",
        RegexOptions.Compiled,
        RegexTimeout);

    // Patterns to extract a title phrase after creation verbs
    // e.g., "create new onboarding tasks for non-technical people" -> "onboarding tasks for non-technical people"
    // Greedy capture: takes everything after the verb + optional fillers to end of string,
    // then we clean up trailing noise in CleanExtractedTitle.
    private static readonly Regex CreateTitlePattern = new(
        @"\b(?:create|add|make|generate|build|prepare|set\s+up)\b(?:\s+(?:a|an|new|some|the|my|few|several|three|two|four|five))?\s+(.+?)\s*[.!?]?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Fallback: extract title after "new" keyword
    // e.g., "I need three new cards for the sprint" -> "cards for the sprint"
    private static readonly Regex NewTitlePattern = new(
        @"\bnew\b\s+(.+?)\s*[.!?]?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Pattern to detect card IDs in the message (for move/archive/update)
    private static readonly Regex CardIdPattern = new(
        @"\b([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Pattern to detect column references
    private static readonly Regex ColumnNamePattern = new(
        @"(?:to|into|in)\s+(?:column\s+)?['""]([^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Pattern to detect column reference without quotes (common phrasing)
    private static readonly Regex ColumnNameUnquotedPattern = new(
        @"(?:to|into)\s+(?:column\s+)(\w[\w\s]*\w)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    /// <summary>
    /// Attempts to extract structured instructions from a natural language message
    /// given a detected action intent.
    /// </summary>
    /// <param name="message">The raw user message.</param>
    /// <param name="actionIntent">The intent detected by <see cref="LlmIntentClassifier"/>.</param>
    /// <returns>A list of structured instructions in parser-compatible format, or empty if extraction fails.</returns>
    public static List<string> Extract(string message, string? actionIntent)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(actionIntent))
            return new List<string>();

        try
        {
            return actionIntent switch
            {
                "card.create" => ExtractCardCreateInstructions(message),
                "card.move" => ExtractCardMoveInstructions(message),
                "card.archive" => ExtractCardArchiveInstructions(message),
                "card.update" => ExtractCardUpdateInstructions(message),
                "board.create" => ExtractBoardCreateInstructions(message),
                "board.update" => ExtractBoardRenameInstructions(message),
                _ => new List<string>()
            };
        }
        catch (RegexMatchTimeoutException)
        {
            return new List<string>();
        }
    }

    private static List<string> ExtractCardCreateInstructions(string message)
    {
        // First, check for a quoted title — this is the most reliable signal
        var quotedMatch = QuotedTitlePattern.Match(message);
        if (quotedMatch.Success)
        {
            var title = quotedMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(title))
                return new List<string> { $"create card \"{title}\"" };
        }

        // Try to extract a meaningful title from the natural language
        var createMatch = CreateTitlePattern.Match(message);
        if (createMatch.Success)
        {
            var rawTitle = createMatch.Groups[1].Value.Trim();
            var title = CleanExtractedTitle(rawTitle);
            if (!string.IsNullOrWhiteSpace(title))
                return new List<string> { $"create card \"{title}\"" };
        }

        // Fallback: try the "new X" pattern
        var newMatch = NewTitlePattern.Match(message);
        if (newMatch.Success)
        {
            var rawTitle = newMatch.Groups[1].Value.Trim();
            var title = CleanExtractedTitle(rawTitle);
            if (!string.IsNullOrWhiteSpace(title))
                return new List<string> { $"create card \"{title}\"" };
        }

        return new List<string>();
    }

    private static List<string> ExtractCardMoveInstructions(string message)
    {
        var cardIdMatch = CardIdPattern.Match(message);
        if (!cardIdMatch.Success)
            return new List<string>();

        var cardId = cardIdMatch.Groups[1].Value;

        // Try quoted column name first
        var columnMatch = ColumnNamePattern.Match(message);
        if (columnMatch.Success)
        {
            var columnName = columnMatch.Groups[1].Value.Trim();
            return new List<string> { $"move card {cardId} to column \"{columnName}\"" };
        }

        return new List<string>();
    }

    private static List<string> ExtractCardArchiveInstructions(string message)
    {
        var cardIdMatch = CardIdPattern.Match(message);
        if (cardIdMatch.Success)
        {
            var cardId = cardIdMatch.Groups[1].Value;
            return new List<string> { $"archive card {cardId}" };
        }

        return new List<string>();
    }

    private static List<string> ExtractCardUpdateInstructions(string message)
    {
        var cardIdMatch = CardIdPattern.Match(message);
        if (!cardIdMatch.Success)
            return new List<string>();

        var cardId = cardIdMatch.Groups[1].Value;

        // Try to find a quoted value for the update
        var quotedMatch = QuotedTitlePattern.Match(message);
        if (quotedMatch.Success)
        {
            var value = quotedMatch.Groups[1].Value.Trim();
            // Determine if updating title or description
            var lower = message.ToLowerInvariant();
            var field = lower.Contains("description") || lower.Contains("desc") ? "description" : "title";
            return new List<string> { $"update card {cardId} {field} \"{value}\"" };
        }

        return new List<string>();
    }

    private static List<string> ExtractBoardCreateInstructions(string message)
    {
        // Try quoted board name
        var quotedMatch = QuotedTitlePattern.Match(message);
        if (quotedMatch.Success)
        {
            var name = quotedMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return new List<string> { $"create card \"{name}\"" };
        }

        // Board creation doesn't have a direct parser pattern in AutomationPlannerService,
        // so we can't produce a structured instruction for it yet
        return new List<string>();
    }

    private static List<string> ExtractBoardRenameInstructions(string message)
    {
        var quotedMatch = QuotedTitlePattern.Match(message);
        if (quotedMatch.Success)
        {
            var name = quotedMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return new List<string> { $"rename board to \"{name}\"" };
        }

        return new List<string>();
    }

    /// <summary>
    /// Cleans an extracted title phrase by removing common filler words and
    /// normalizing to title case.
    /// </summary>
    internal static string CleanExtractedTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return string.Empty;

        var title = rawTitle.Trim();

        // Remove trailing noise words (common sentence-end patterns)
        title = Regex.Replace(title, @"\s+(please|plz|pls|thanks|thx|asap)\s*$", "", RegexOptions.IgnoreCase);

        // Remove leading filler: "a ", "an ", "some ", "the "
        title = Regex.Replace(title, @"^(a|an|some|the|my|our)\s+", "", RegexOptions.IgnoreCase);

        // Remove generic noun suffixes when they ARE the entire remaining text
        // e.g., "cards" alone is not a useful title, but "onboarding cards" is
        if (Regex.IsMatch(title, @"^(cards?|tasks?|items?)$", RegexOptions.IgnoreCase))
            return string.Empty;

        // Capitalize first letter
        if (title.Length > 0)
            title = char.ToUpperInvariant(title[0]) + title[1..];

        return title;
    }
}
