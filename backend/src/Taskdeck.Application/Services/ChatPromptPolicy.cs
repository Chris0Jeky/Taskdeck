using System.Text.RegularExpressions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Pure prompt classification and checklist parsing rules used by ChatService.
/// Keeping these rules outside the orchestration service makes their boundary
/// directly testable without constructing providers, repositories or sessions.
/// </summary>
public static class ChatPromptPolicy
{
    private static readonly string[] PromptInjectionDenylist =
    {
        "ignore previous instructions",
        "reveal system prompt",
        "rm -rf",
        "drop table",
        "delete every board"
    };

    private static readonly Regex ChecklistRequestRegex = new(
        @"(?m)^\s*[-*]\s*\[\s\]\s+.+$",
        RegexOptions.Compiled);

    private static readonly Regex ChecklistLineRegex = new(
        @"^\s*[-*]\s*\[\s\]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    public static bool ContainsBlockedPromptPattern(string content)
    {
        var normalized = content.ToLowerInvariant();
        return PromptInjectionDenylist.Any(pattern => normalized.Contains(pattern, StringComparison.Ordinal));
    }

    public static bool LooksLikeChecklistBootstrapRequest(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        return ChecklistRequestRegex.IsMatch(content);
    }

    public static bool StartsWithQuestion(string content)
    {
        var firstLine = content
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));
        return firstLine?.TrimEnd().EndsWith("?", StringComparison.Ordinal) == true;
    }

    public static List<string> ParseChecklistItems(string content)
    {
        var items = new List<string>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var match = ChecklistLineRegex.Match(line);
            if (!match.Success)
                continue;

            var title = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(title))
                items.Add(title);
        }

        return items;
    }
}
