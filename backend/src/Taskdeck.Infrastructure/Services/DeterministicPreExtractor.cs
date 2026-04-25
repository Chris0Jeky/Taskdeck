using System.Text.RegularExpressions;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;
using Microsoft.Recognizers.Text.Number;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Extracts deterministic structured data (dates, numbers, durations, URLs, emails)
/// from free text using Microsoft.Recognizers.Text.
///
/// This runs entirely locally -- no LLM calls, no network access.
/// Handles malformed input gracefully by returning empty results.
/// </summary>
public class DeterministicPreExtractor : IDeterministicPreExtractor
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    public IReadOnlyList<ExtractedEntity> Extract(string text, string culture = "en-us")
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<ExtractedEntity>();

        var results = new List<ExtractedEntity>();

        // Date/time and duration recognition (single call, split by type)
        try
        {
            var dateTimeResults = DateTimeRecognizer.RecognizeDateTime(text, culture);
            foreach (var result in dateTimeResults)
            {
                var isDuration = result.TypeName.Contains("duration", StringComparison.OrdinalIgnoreCase);
                var entityType = isDuration ? "Duration" : "DateTime";
                var resolved = ResolveValue(result);
                // ModelResult.End is the last character index (inclusive), so +1 for exclusive end
                results.Add(new ExtractedEntity(
                    entityType,
                    result.Text,
                    resolved,
                    result.Start,
                    result.End + 1));
            }
        }
        catch
        {
            // Malformed input -- skip datetime/duration extraction
        }

        // Number recognition
        try
        {
            var numberResults = NumberRecognizer.RecognizeNumber(text, culture);
            foreach (var result in numberResults)
            {
                var resolved = ResolveValue(result);
                results.Add(new ExtractedEntity(
                    "Number",
                    result.Text,
                    resolved,
                    result.Start,
                    result.End + 1));
            }
        }
        catch
        {
            // Malformed input -- skip number extraction
        }

        // URL extraction (regex-based, more reliable than Recognizers for URLs)
        try
        {
            var urlMatches = UrlRegex.Matches(text);
            foreach (Match match in urlMatches)
            {
                var url = TrimTrailingUrlPunctuation(match.Value);
                if (url.Length == 0)
                    continue;

                results.Add(new ExtractedEntity(
                    "Url",
                    url,
                    url,
                    match.Index,
                    match.Index + url.Length));
            }
        }
        catch
        {
            // Regex failure -- skip URL extraction
        }

        // Email extraction (regex-based)
        try
        {
            var emailMatches = EmailRegex.Matches(text);
            foreach (Match match in emailMatches)
            {
                // Skip if already captured as part of a URL
                if (results.Any(r => r.EntityType == "Url" && r.Start <= match.Index && r.End >= match.Index + match.Length))
                    continue;

                results.Add(new ExtractedEntity(
                    "Email",
                    match.Value,
                    match.Value,
                    match.Index,
                    match.Index + match.Length));
            }
        }
        catch
        {
            // Regex failure -- skip email extraction
        }

        return results;
    }

    private static string ResolveValue(ModelResult result)
    {
        if (result.Resolution == null)
            return result.Text;

        // Microsoft.Recognizers.Text returns resolution as a dictionary
        // with a "values" key containing a list of resolution objects
        if (result.Resolution.TryGetValue("values", out var valuesObj) && valuesObj is IList<Dictionary<string, string>> values)
        {
            if (values.Count > 0)
            {
                // Prefer "value" key, then "timex", then first available value
                var firstValue = values[0];
                if (firstValue.TryGetValue("value", out var value))
                    return value;
                if (firstValue.TryGetValue("timex", out var timex))
                    return timex;
                return firstValue.Values.FirstOrDefault() ?? result.Text;
            }
        }

        // Fallback: try "value" directly
        if (result.Resolution.TryGetValue("value", out var directValue) && directValue is string strValue)
            return strValue;

        return result.Text;
    }

    private static string TrimTrailingUrlPunctuation(string url)
    {
        var end = url.Length;
        while (end > 0)
        {
            var c = url[end - 1];
            if (c is '.' or ',' or ';' or ':' or '!' or '?')
            {
                end--;
                continue;
            }

            if (c == ')' && Count(url, end, ')') > Count(url, end, '('))
            {
                end--;
                continue;
            }

            if (c == ']' && Count(url, end, ']') > Count(url, end, '['))
            {
                end--;
                continue;
            }

            break;
        }

        return end == url.Length ? url : url[..end];
    }

    private static int Count(string value, int length, char target)
    {
        var count = 0;
        for (var i = 0; i < length; i++)
        {
            if (value[i] == target)
                count++;
        }

        return count;
    }
}
