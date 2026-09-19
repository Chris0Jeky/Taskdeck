namespace Taskdeck.Application.Services.Pipeline;

/// <summary>
/// Canonical target/action vocabulary accepted by proposal execution. Conflict review
/// uses the same vocabulary so a syntactically valid future verb cannot be presented as
/// fully evaluated before the validator and dispatcher know how to handle it.
///
/// Values are case-insensitive but deliberately not trimmed: the dispatcher normalizes
/// casing only, so whitespace-wrapped values are not executable and must fail closed here.
/// </summary>
internal static class ProposalOperationVocabulary
{
    private static readonly HashSet<string> CardActions = new(StringComparer.Ordinal)
    {
        "create",
        "update",
        "move",
        "archive",
        "delete",
        "archive-lifecycle",
        "restore-lifecycle",
        "add-relation",
        "remove-relation",
        ProposalAssignmentContract.Action
    };

    public static bool IsSupported(string? targetType, string? actionType)
    {
        if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(actionType))
            return false;

        var normalizedTarget = targetType.ToLowerInvariant();
        var normalizedAction = actionType.ToLowerInvariant();

        return normalizedTarget switch
        {
            "card" => CardActions.Contains(normalizedAction) ||
                      CardLabelOperationVocabulary.Classify(normalizedAction) is
                          CardLabelOperationAction.Add or CardLabelOperationAction.Remove,
            "board" => normalizedAction == "update",
            "column" => normalizedAction is "create" or "reorder",
            _ => false
        };
    }

    public static string GetUnsupportedMessage(string targetType, string actionType)
    {
        return targetType.ToLowerInvariant() switch
        {
            "card" => $"Unsupported card action: {actionType}",
            "board" => $"Unsupported board action: {actionType}",
            "column" => $"Unsupported column action: {actionType}",
            _ => $"Unsupported target type: {targetType}"
        };
    }
}
