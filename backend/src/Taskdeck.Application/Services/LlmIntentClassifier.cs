namespace Taskdeck.Application.Services;

public static class LlmIntentClassifier
{
    public static (bool IsActionable, string? ActionIntent) Classify(string message)
    {
        var lower = message.ToLowerInvariant();

        if (lower.Contains("create card") || lower.Contains("add card"))
            return (true, "card.create");
        if (lower.Contains("move card"))
            return (true, "card.move");
        if (lower.Contains("archive card") || lower.Contains("delete card"))
            return (true, "card.archive");
        if (lower.Contains("update card") || lower.Contains("edit card"))
            return (true, "card.update");
        if (lower.Contains("create board") || lower.Contains("add board"))
            return (true, "board.create");
        if (lower.Contains("reorder") || lower.Contains("sort"))
            return (true, "column.reorder");

        return (false, null);
    }
}
