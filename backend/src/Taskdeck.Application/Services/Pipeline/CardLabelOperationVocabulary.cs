namespace Taskdeck.Application.Services.Pipeline;

internal enum CardLabelOperationAction
{
    None,
    Add,
    Remove,
    InvalidAlias
}

/// <summary>
/// Keeps the accepted card-label action aliases identical at validation, preview,
/// and execution. Shape validation remains deliberately open-ended; this helper
/// only prevents label-like spellings from being presented as executable when the
/// registry does not accept them.
/// </summary>
internal static class CardLabelOperationVocabulary
{
    public static CardLabelOperationAction Classify(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            return CardLabelOperationAction.None;

        var normalized = actionType.ToLowerInvariant();
        var exactMatch = normalized switch
        {
            "add-label" or "add_label" or "addlabel" => CardLabelOperationAction.Add,
            "remove-label" or "remove_label" or "removelabel" => CardLabelOperationAction.Remove,
            _ => CardLabelOperationAction.None
        };
        if (exactMatch != CardLabelOperationAction.None)
            return exactMatch;

        var collapsed = normalized.Trim()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(".", string.Empty);
        return collapsed is "addlabel" or "removelabel"
            ? CardLabelOperationAction.InvalidAlias
            : CardLabelOperationAction.None;
    }
}
