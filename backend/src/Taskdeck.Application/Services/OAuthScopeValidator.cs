using Microsoft.Extensions.Logging;

namespace Taskdeck.Application.Services;

/// <summary>
/// Validates OAuth scopes granted by external providers against required and expected scopes.
/// GitHub returns granted scopes as a comma-separated list in the token response body.
/// </summary>
public class OAuthScopeValidator
{
    private readonly ILogger<OAuthScopeValidator> _logger;

    public OAuthScopeValidator(ILogger<OAuthScopeValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates the granted scopes against the configured required and expected scopes.
    /// </summary>
    /// <param name="grantedScopesHeader">
    /// The raw scope string from the provider (e.g. from GitHub's token response "scope" field).
    /// GitHub uses comma-separated scopes; this method handles both comma and space separators.
    /// </param>
    /// <param name="requiredScopes">Scopes that must be present — authentication fails if any are missing.</param>
    /// <param name="expectedScopes">Scopes that should be present — a warning is logged if missing, but auth proceeds.</param>
    /// <returns>A result indicating whether validation passed, with details about any issues.</returns>
    public OAuthScopeValidationResult Validate(
        string? grantedScopesHeader,
        IReadOnlyList<string>? requiredScopes,
        IReadOnlyList<string>? expectedScopes)
    {
        var grantedScopes = ParseScopes(grantedScopesHeader);
        // GitHub scopes are case-sensitive (e.g. "read:user" != "Read:User").
        // Use Ordinal comparison to match exactly what the provider returns.
        var grantedSet = new HashSet<string>(grantedScopes, StringComparer.Ordinal);

        var effectiveRequired = requiredScopes ?? Array.Empty<string>();
        var effectiveExpected = expectedScopes ?? Array.Empty<string>();

        // Check required scopes
        var missingRequired = effectiveRequired
            .Where(scope => !grantedSet.Contains(scope))
            .ToList();

        if (missingRequired.Count > 0)
        {
            // Caller in AuthenticationRegistration logs a terminal LogError;
            // this LogWarning provides structured detail for diagnostics.
            _logger.LogWarning(
                "OAuth scope validation failed: required scopes missing. Required: [{RequiredScopes}], Granted: [{GrantedScopes}], Missing: [{MissingScopes}]",
                string.Join(", ", effectiveRequired),
                string.Join(", ", grantedScopes),
                string.Join(", ", missingRequired));

            return OAuthScopeValidationResult.Failed(missingRequired, grantedScopes);
        }

        // Check expected (non-required) scopes
        var missingExpected = effectiveExpected
            .Where(scope => !grantedSet.Contains(scope))
            .ToList();

        if (missingExpected.Count > 0)
        {
            _logger.LogWarning(
                "OAuth scope validation warning: expected scopes missing. Expected: [{ExpectedScopes}], Granted: [{GrantedScopes}], Missing: [{MissingScopes}]. Authentication will proceed.",
                string.Join(", ", effectiveExpected),
                string.Join(", ", grantedScopes),
                string.Join(", ", missingExpected));
        }

        return OAuthScopeValidationResult.Succeeded(grantedScopes, missingExpected);
    }

    /// <summary>
    /// Parses a scope string into a list of individual scopes.
    /// Handles GitHub's comma-separated format and standard space-separated format.
    /// Strips tabs and other whitespace to guard against malformed responses.
    /// </summary>
    internal static List<string> ParseScopes(string? scopeHeader)
    {
        if (string.IsNullOrWhiteSpace(scopeHeader))
            return new List<string>();

        // GitHub uses comma-separated scopes (e.g. "read:user, user:email")
        // OAuth2 standard uses space-separated scopes
        // Handle both by splitting on commas, spaces, and tabs, then trimming
        var scopes = scopeHeader
            .Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return scopes;
    }
}

/// <summary>
/// Result of OAuth scope validation.
/// </summary>
public class OAuthScopeValidationResult
{
    public bool IsValid { get; private init; }
    public IReadOnlyList<string> GrantedScopes { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingRequiredScopes { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingExpectedScopes { get; private init; } = Array.Empty<string>();

    /// <summary>
    /// A user-facing error message when validation fails. Null when valid.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    public static OAuthScopeValidationResult Failed(
        IReadOnlyList<string> missingRequired,
        IReadOnlyList<string> grantedScopes)
    {
        var scopeList = string.Join(", ", missingRequired);
        return new OAuthScopeValidationResult
        {
            IsValid = false,
            GrantedScopes = grantedScopes,
            MissingRequiredScopes = missingRequired,
            MissingExpectedScopes = Array.Empty<string>(),
            ErrorMessage = $"GitHub did not grant the required OAuth scopes: {scopeList}. " +
                           "Please re-authorize the application with the required permissions."
        };
    }

    public static OAuthScopeValidationResult Succeeded(
        IReadOnlyList<string> grantedScopes,
        IReadOnlyList<string> missingExpected)
    {
        return new OAuthScopeValidationResult
        {
            IsValid = true,
            GrantedScopes = grantedScopes,
            MissingRequiredScopes = Array.Empty<string>(),
            MissingExpectedScopes = missingExpected,
            ErrorMessage = null
        };
    }
}
