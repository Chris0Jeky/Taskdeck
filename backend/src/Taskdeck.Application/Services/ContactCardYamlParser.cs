using Taskdeck.Application.DTOs;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Parses and serializes YAML front matter in card descriptions for the
/// card-first Outreach CRM contact model.
///
/// Front matter is delimited by a pair of <c>---</c> lines at the start
/// of the description. Content after the closing delimiter is preserved
/// as the body (timeline, freeform notes, etc.).
/// </summary>
public static class ContactCardYamlParser
{
    private const string FrontMatterDelimiter = "---";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly HashSet<string> ValidTiers =
        new(StringComparer.OrdinalIgnoreCase) { "A", "B", "C" };

    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "cold", "warm", "active", "referral", "interviewing", "closed"
        };

    /// <summary>
    /// Result of parsing a card description that may contain YAML front matter.
    /// </summary>
    /// <param name="FrontMatter">Parsed contact fields, or null when no front matter is present.</param>
    /// <param name="Body">Content after the closing front matter delimiter (may be empty).</param>
    /// <param name="Errors">Validation/parsing errors. Empty list means success.</param>
    public sealed record ParseResult(
        ContactCardFrontMatter? FrontMatter,
        string Body,
        IReadOnlyList<string> Errors);

    /// <summary>
    /// Parse a card description, extracting YAML front matter and body content.
    /// Returns explicit errors for malformed YAML rather than throwing exceptions.
    /// </summary>
    public static ParseResult Parse(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return new ParseResult(null, string.Empty, Array.Empty<string>());
        }

        var (yamlBlock, body, extractionError) = ExtractFrontMatterBlock(description);

        if (extractionError is not null)
        {
            return new ParseResult(null, description, new[] { extractionError });
        }

        if (yamlBlock is null)
        {
            // No front matter present — not an error, just a plain description.
            return new ParseResult(null, description, Array.Empty<string>());
        }

        try
        {
            var frontMatter = Deserializer.Deserialize<ContactCardFrontMatter>(yamlBlock);

            if (frontMatter is null)
            {
                return new ParseResult(null, body, new[] { "YAML front matter block is empty." });
            }

            var validationErrors = Validate(frontMatter);
            if (validationErrors.Count > 0)
            {
                return new ParseResult(frontMatter, body, validationErrors);
            }

            return new ParseResult(frontMatter, body, Array.Empty<string>());
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            var message = $"Invalid YAML in front matter: {ex.InnerException?.Message ?? ex.Message}";
            return new ParseResult(null, body, new[] { message });
        }
    }

    /// <summary>
    /// Serialize a <see cref="ContactCardFrontMatter"/> and body back into
    /// a card description string with YAML front matter delimiters.
    /// </summary>
    public static string Serialize(ContactCardFrontMatter frontMatter, string? body = null)
    {
        ArgumentNullException.ThrowIfNull(frontMatter);

        var yaml = YamlSerializer.Serialize(frontMatter).TrimEnd();

        var parts = new List<string>
        {
            FrontMatterDelimiter,
            yaml,
            FrontMatterDelimiter
        };

        if (!string.IsNullOrEmpty(body))
        {
            parts.Add(body);
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Validate structural constraints on the front matter fields.
    /// Returns an empty list when valid.
    /// </summary>
    internal static IReadOnlyList<string> Validate(ContactCardFrontMatter fm)
    {
        var errors = new List<string>();

        if (!string.IsNullOrEmpty(fm.Type)
            && !string.Equals(fm.Type, "contact", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Unsupported front matter type '{fm.Type}'. Expected 'contact'.");
        }

        if (!string.IsNullOrEmpty(fm.RelationshipTier) && !ValidTiers.Contains(fm.RelationshipTier))
        {
            errors.Add($"Invalid relationship_tier '{fm.RelationshipTier}'. Expected one of: A, B, C.");
        }

        if (!string.IsNullOrEmpty(fm.Status) && !ValidStatuses.Contains(fm.Status))
        {
            errors.Add($"Invalid status '{fm.Status}'. Expected one of: cold, warm, active, referral, interviewing, closed.");
        }

        if (!string.IsNullOrEmpty(fm.LastTouchAt) && !DateOnly.TryParse(fm.LastTouchAt, out _))
        {
            errors.Add($"Invalid last_touch_at format '{fm.LastTouchAt}'. Expected ISO 8601 date (YYYY-MM-DD).");
        }

        if (!string.IsNullOrEmpty(fm.NextTouchAt) && !DateOnly.TryParse(fm.NextTouchAt, out _))
        {
            errors.Add($"Invalid next_touch_at format '{fm.NextTouchAt}'. Expected ISO 8601 date (YYYY-MM-DD).");
        }

        return errors;
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Extract the YAML block and body from a description string.
    /// Returns (yamlBlock, body, error). When no front matter delimiters
    /// are found, yamlBlock is null and body equals the full description.
    /// </summary>
    private static (string? YamlBlock, string Body, string? Error) ExtractFrontMatterBlock(string description)
    {
        // Normalise line endings to \n for consistent splitting.
        var normalised = description.Replace("\r\n", "\n").Replace("\r", "\n");

        // Front matter must start at the very beginning of the description.
        if (!normalised.StartsWith(FrontMatterDelimiter, StringComparison.Ordinal))
        {
            return (null, description, null);
        }

        // Find the first line after the opening delimiter.
        var firstNewline = normalised.IndexOf('\n');
        if (firstNewline < 0)
        {
            // Only "---" with no content at all.
            return (null, description, null);
        }

        // Verify the opening line is exactly "---" (possibly with trailing whitespace).
        var openingLine = normalised[..firstNewline].TrimEnd();
        if (openingLine != FrontMatterDelimiter)
        {
            return (null, description, null);
        }

        // Search for the closing "---" line.
        var searchStart = firstNewline + 1;
        var closingIndex = -1;

        while (searchStart < normalised.Length)
        {
            var lineEnd = normalised.IndexOf('\n', searchStart);
            var line = lineEnd >= 0
                ? normalised[searchStart..lineEnd]
                : normalised[searchStart..];

            if (line.TrimEnd() == FrontMatterDelimiter)
            {
                closingIndex = searchStart;
                break;
            }

            if (lineEnd < 0)
            {
                break;
            }

            searchStart = lineEnd + 1;
        }

        if (closingIndex < 0)
        {
            return (null, description, "Opening '---' found but no closing '---' delimiter.");
        }

        var yamlBlock = normalised[(firstNewline + 1)..closingIndex];
        var afterClosing = normalised.IndexOf('\n', closingIndex);
        var body = afterClosing >= 0 && afterClosing + 1 < normalised.Length
            ? normalised[(afterClosing + 1)..]
            : string.Empty;

        return (yamlBlock, body, null);
    }
}
