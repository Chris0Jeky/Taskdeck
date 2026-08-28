using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Enums;

/// <summary>
/// Validates and converts API key capability masks at trust boundaries.
/// </summary>
public static class ApiKeyScopeRules
{
    private const string ValidationMessage = "API key scopes must contain only known, non-empty values";

    public static bool IsValid(ApiKeyScope scopes) =>
        scopes != ApiKeyScope.None && (scopes & ~ApiKeyScope.Full) == ApiKeyScope.None;

    public static bool Includes(ApiKeyScope granted, ApiKeyScope required) =>
        IsValid(granted)
        && IsValid(required)
        && (granted & required) == required;

    public static void EnsureValid(ApiKeyScope scopes)
    {
        if (!IsValid(scopes))
            throw new DomainException(ErrorCodes.ValidationError, ValidationMessage);
    }

    public static bool TryParseNames(IEnumerable<string>? names, out ApiKeyScope scopes)
    {
        scopes = ApiKeyScope.None;
        if (names is null)
            return false;

        var parsedScopes = ApiKeyScope.None;
        var hasValue = false;
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var parsed = name.Trim().ToLowerInvariant() switch
            {
                "read" => ApiKeyScope.Read,
                "propose" => ApiKeyScope.Propose,
                "manage" => ApiKeyScope.Manage,
                _ => ApiKeyScope.None
            };

            if (parsed == ApiKeyScope.None)
                return false;

            parsedScopes |= parsed;
            hasValue = true;
        }

        if (!hasValue || !IsValid(parsedScopes))
            return false;

        scopes = parsedScopes;
        return true;
    }

    public static IReadOnlyList<string> ToNames(ApiKeyScope scopes)
    {
        EnsureValid(scopes);

        var names = new List<string>(3);
        if (Includes(scopes, ApiKeyScope.Read))
            names.Add("read");
        if (Includes(scopes, ApiKeyScope.Propose))
            names.Add("propose");
        if (Includes(scopes, ApiKeyScope.Manage))
            names.Add("manage");

        return names;
    }
}
