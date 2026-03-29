using System.Text.RegularExpressions;

namespace Taskdeck.Application.Services;

public static class LlmIntentClassifier
{
    // Timeout to prevent catastrophic backtracking
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

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

    // Card move — "move" + optional words + "card/task", or "move" + card/task context + "to"
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

    public static (bool IsActionable, string? ActionIntent) Classify(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return (false, null);

        var lower = message.ToLowerInvariant();

        // Check negative context first — suppress if negated or about another tool
        if (IsNegativeContext(lower, message))
            return (false, null);

        // Archive/delete/remove must be checked BEFORE move to fix the
        // "remove card" substring bug (remove contains "move")
        if (MatchesCardArchive(lower))
            return (true, "card.archive");

        if (MatchesCardMove(lower))
            return (true, "card.move");

        if (MatchesCardUpdate(lower))
            return (true, "card.update");

        if (MatchesCardCreate(lower))
            return (true, "card.create");

        if (MatchesBoardCreate(lower))
            return (true, "board.create");

        if (MatchesBoardRename(lower))
            return (true, "board.update");

        if (MatchesReorder(lower))
            return (true, "column.reorder");

        return (false, null);
    }

    private static bool IsNegativeContext(string lower, string original)
    {
        try
        {
            // Negation: "don't create task yet"
            if (NegationPattern.IsMatch(lower))
                return true;

            // Asking about another tool: "how do I create a card in Jira?"
            if (OtherToolPattern.IsMatch(lower) && QuestionAboutHowPattern.IsMatch(original.Trim()))
                return true;
        }
        catch (RegexMatchTimeoutException)
        {
            // On timeout, fall through to normal classification
        }

        return false;
    }

    private static bool MatchesCardCreate(string lower)
    {
        // Exact substring matches (backward compatible)
        if (lower.Contains("create card") || lower.Contains("add card")
            || lower.Contains("create a card") || lower.Contains("add a card")
            || lower.Contains("create task") || lower.Contains("add task")
            || lower.Contains("create a task") || lower.Contains("add a task")
            || lower.Contains("new card") || lower.Contains("new task")
            || lower.Contains("make a card") || lower.Contains("make a task")
            || lower.Contains("make card") || lower.Contains("make task"))
            return true;

        // Regex-based natural language matching
        try
        {
            if (CardCreatePattern.IsMatch(lower))
                return true;
            if (NewCardPattern.IsMatch(lower))
                return true;
        }
        catch (RegexMatchTimeoutException)
        {
            // Fall through — don't match on timeout
        }

        return false;
    }

    private static bool MatchesCardMove(string lower)
    {
        if (lower.Contains("move card") || lower.Contains("move task"))
            return true;

        try
        {
            if (CardMovePattern.IsMatch(lower))
                return true;
        }
        catch (RegexMatchTimeoutException) { }

        return false;
    }

    private static bool MatchesCardArchive(string lower)
    {
        if (lower.Contains("archive card") || lower.Contains("delete card")
            || lower.Contains("remove card") || lower.Contains("archive task")
            || lower.Contains("delete task") || lower.Contains("remove task"))
            return true;

        try
        {
            if (CardArchivePattern.IsMatch(lower))
                return true;
        }
        catch (RegexMatchTimeoutException) { }

        return false;
    }

    private static bool MatchesCardUpdate(string lower)
    {
        if (lower.Contains("update card") || lower.Contains("edit card")
            || lower.Contains("rename card") || lower.Contains("update task")
            || lower.Contains("edit task") || lower.Contains("rename task"))
            return true;

        try
        {
            if (CardUpdatePattern.IsMatch(lower))
                return true;
        }
        catch (RegexMatchTimeoutException) { }

        return false;
    }

    private static bool MatchesBoardCreate(string lower)
    {
        if (lower.Contains("create board") || lower.Contains("add board")
            || lower.Contains("new board"))
            return true;

        try
        {
            if (BoardCreatePattern.IsMatch(lower))
                return true;
        }
        catch (RegexMatchTimeoutException) { }

        return false;
    }

    private static bool MatchesBoardRename(string lower)
    {
        if (lower.Contains("rename board"))
            return true;

        try
        {
            if (BoardRenamePattern.IsMatch(lower))
                return true;
        }
        catch (RegexMatchTimeoutException) { }

        return false;
    }

    private static bool MatchesReorder(string lower)
    {
        if (lower.Contains("reorder cards") || lower.Contains("reorder column")
            || lower.Contains("reorder columns") || lower.Contains("reorder board")
            || lower.Contains("sort cards") || lower.Contains("sort column")
            || lower.Contains("sort columns") || lower.Contains("sort board"))
            return true;

        try
        {
            if (ReorderPattern.IsMatch(lower))
                return true;
        }
        catch (RegexMatchTimeoutException) { }

        return false;
    }
}
