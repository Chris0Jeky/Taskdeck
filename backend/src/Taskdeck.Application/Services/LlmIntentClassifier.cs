using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Taskdeck.Application.Services;

public static class LlmIntentClassifier
{
    // Timeout to prevent catastrophic backtracking.
    // All patterns use bounded quantifiers ({0,4}/{0,6}), so true catastrophic
    // backtracking is not possible. The timeout is set generously (2 s) to avoid
    // false RegexMatchTimeoutExceptions on CPU-throttled CI runners while still
    // guarding against adversarial inputs.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(2000);

    // Negative context patterns — suppress matches in these contexts
    private static readonly Regex NegationPattern = new(
        @"\b(don'?t|do not|never|stop|cancel|undo|avoid)\b(\s+\w+){0,6}\s+\b(create|add|make|move|archive|delete|remove|update|edit|rename|generate|build|set up|prepare)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex OtherToolPattern = new(
        @"\b(in|for|with|using|on)\s+(jira|trello|asana|notion|monday|clickup|linear|github issues|azure devops)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex QuestionAboutHowPattern = new(
        @"^\s*(how|what|where|when|why|can\s+i|is\s+it)\b.*\?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Card creation — verbs followed by optional words then card/task nouns
    private static readonly Regex CardCreatePattern = new(
        @"\b(create|add|make|generate|build|prepare|set\s+up)\b(\s+\w+){0,5}\s+\b(cards?|tasks?|items?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // "new card/task" with optional words between
    private static readonly Regex NewCardPattern = new(
        @"\b(new)\b(\s+\w+){0,4}\s+\b(cards?|tasks?|items?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Card move — "move" + optional words + "card/task"
    private static readonly Regex CardMovePattern = new(
        @"\bmove\b(\s+\w+){0,4}\s+\b(cards?|tasks?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Card archive — "archive/delete/remove" + optional words + "card/task"
    private static readonly Regex CardArchivePattern = new(
        @"\b(archive|delete|remove)\b(\s+\w+){0,4}\s+\b(cards?|tasks?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Card update — "update/edit/rename" + optional words + "card/task"
    private static readonly Regex CardUpdatePattern = new(
        @"\b(update|edit|rename|modify|change)\b(\s+\w+){0,4}\s+\b(cards?|tasks?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Board creation — verbs + optional words + "board"
    private static readonly Regex BoardCreatePattern = new(
        @"\b(create|add|make|generate|build|prepare|set\s+up|new)\b(\s+\w+){0,4}\s+\b(boards?|project\s+boards?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Board rename
    private static readonly Regex BoardRenamePattern = new(
        @"\b(rename|update|edit)\b(\s+\w+){0,4}\s+\bboards?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Column reorder / sort
    private static readonly Regex ReorderPattern = new(
        @"\b(reorder|sort|rearrange|reorganize)\b(\s+\w+){0,4}\s+\b(cards?|columns?|boards?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    public static (bool IsActionable, string? ActionIntent) Classify(
        string message,
        ILogger? logger = null)
        => ClassifyCore(
            message,
            logger,
            static (pattern, input) => pattern.IsMatch(input));

    internal static (bool IsActionable, string? ActionIntent) ClassifyForTests(
        string message,
        ILogger? logger,
        Func<Regex, string, bool> matcher)
        => ClassifyCore(message, logger, matcher);

    private static (bool IsActionable, string? ActionIntent) ClassifyCore(
        string message,
        ILogger? logger,
        Func<Regex, string, bool> matcher)
    {
        if (string.IsNullOrWhiteSpace(message))
            return (false, null);

        var lower = message.ToLowerInvariant();
        var timeoutReported = false;

        // Check negative context first — suppress if negated or about another tool
        if (IsNegativeContext(lower, message, logger, ref timeoutReported, matcher))
            return (false, null);

        // Archive/delete/remove must be checked BEFORE move to fix the
        // "remove card" substring bug (remove contains "move")
        if (MatchesCardArchive(lower, logger, ref timeoutReported, matcher))
            return (true, "card.archive");

        if (MatchesCardMove(lower, logger, ref timeoutReported, matcher))
            return (true, "card.move");

        if (MatchesCardUpdate(lower, logger, ref timeoutReported, matcher))
            return (true, "card.update");

        if (MatchesCardCreate(lower, logger, ref timeoutReported, matcher))
            return (true, "card.create");

        if (MatchesBoardCreate(lower, logger, ref timeoutReported, matcher))
            return (true, "board.create");

        if (MatchesBoardRename(lower, logger, ref timeoutReported, matcher))
            return (true, "board.update");

        if (MatchesReorder(lower, logger, ref timeoutReported, matcher))
            return (true, "column.reorder");

        return (false, null);
    }

    private static bool IsNegativeContext(
        string lower,
        string original,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
    {
        // Negation: "don't create task yet"
        if (TryMatch(
                NegationPattern,
                lower,
                "negative-context.negation",
                logger,
                ref timeoutReported,
                matcher))
            return true;

        // Asking about another tool: "how do I create a card in Jira?"
        return TryMatch(
                   OtherToolPattern,
                   lower,
                   "negative-context.other-tool",
                   logger,
                   ref timeoutReported,
                   matcher)
               && TryMatch(
                   QuestionAboutHowPattern,
                   original.Trim(),
                   "negative-context.question",
                   logger,
                   ref timeoutReported,
                   matcher);
    }

    private static bool MatchesCardCreate(
        string lower,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
        => TryMatch(
               CardCreatePattern,
               lower,
               "card-create",
               logger,
               ref timeoutReported,
               matcher)
           || TryMatch(
               NewCardPattern,
               lower,
               "card-create.new",
               logger,
               ref timeoutReported,
               matcher);

    private static bool MatchesCardMove(
        string lower,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
        => TryMatch(
            CardMovePattern,
            lower,
            "card-move",
            logger,
            ref timeoutReported,
            matcher);

    private static bool MatchesCardArchive(
        string lower,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
        => TryMatch(
            CardArchivePattern,
            lower,
            "card-archive",
            logger,
            ref timeoutReported,
            matcher);

    private static bool MatchesCardUpdate(
        string lower,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
        => TryMatch(
            CardUpdatePattern,
            lower,
            "card-update",
            logger,
            ref timeoutReported,
            matcher);

    private static bool MatchesBoardCreate(
        string lower,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
        => TryMatch(
            BoardCreatePattern,
            lower,
            "board-create",
            logger,
            ref timeoutReported,
            matcher);

    private static bool MatchesBoardRename(
        string lower,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
        => TryMatch(
            BoardRenamePattern,
            lower,
            "board-rename",
            logger,
            ref timeoutReported,
            matcher);

    private static bool MatchesReorder(
        string lower,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
        => TryMatch(
            ReorderPattern,
            lower,
            "column-reorder",
            logger,
            ref timeoutReported,
            matcher);

    private static bool TryMatch(
        Regex pattern,
        string input,
        string ruleId,
        ILogger? logger,
        ref bool timeoutReported,
        Func<Regex, string, bool> matcher)
    {
        try
        {
            return matcher(pattern, input);
        }
        catch (RegexMatchTimeoutException)
        {
            if (!timeoutReported)
            {
                logger?.LogWarning(
                    "LLM intent classification regex timed out for rule {RuleId}",
                    ruleId);
                timeoutReported = true;
            }

            // Preserve the classifier's existing fallback outcome: a timeout
            // cannot make a request actionable, but it must not crash the turn.
            return false;
        }
    }
}
