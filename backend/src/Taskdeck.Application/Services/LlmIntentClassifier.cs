namespace Taskdeck.Application.Services;

public static class LlmIntentClassifier
{
    public static (bool IsActionable, string? ActionIntent) Classify(string message)
    {
        var lower = message.ToLowerInvariant();

        // Card creation — explicit commands and natural language
        if (lower.Contains("create card") || lower.Contains("add card")
            || lower.Contains("create a card") || lower.Contains("add a card")
            || lower.Contains("create task") || lower.Contains("add task")
            || lower.Contains("create a task") || lower.Contains("add a task")
            || lower.Contains("new card") || lower.Contains("new task")
            || lower.Contains("make a card") || lower.Contains("make a task")
            || lower.Contains("make card") || lower.Contains("make task"))
            return (true, "card.create");

        if (lower.Contains("move card"))
            return (true, "card.move");
        if (lower.Contains("archive card") || lower.Contains("delete card")
            || lower.Contains("remove card"))
            return (true, "card.archive");
        if (lower.Contains("update card") || lower.Contains("edit card")
            || lower.Contains("rename card"))
            return (true, "card.update");
        if (lower.Contains("create board") || lower.Contains("add board")
            || lower.Contains("new board"))
            return (true, "board.create");
        if (lower.Contains("rename board"))
            return (true, "board.update");
        if (lower.Contains("reorder cards") || lower.Contains("reorder column")
            || lower.Contains("reorder columns") || lower.Contains("reorder board")
            || lower.Contains("sort cards") || lower.Contains("sort column")
            || lower.Contains("sort columns") || lower.Contains("sort board"))
            return (true, "column.reorder");

        return (false, null);
    }
}
