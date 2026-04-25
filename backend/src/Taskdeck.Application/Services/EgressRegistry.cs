namespace Taskdeck.Application.Services;

/// <summary>
/// Concrete egress registry seeded with all known outbound paths.
/// Implements GP-10: every external destination must be disclosed/enforced.
///
/// Seed entries cover:
/// - OpenAI API (LLM provider)
/// - Google Gemini API (LLM provider)
/// - Configured outbound webhook destinations
/// - Self-hosted analytics (Plausible/Umami)
/// - Sentry error reporting
///
/// Additional entries can be registered at startup when new connectors or
/// providers are configured.
/// </summary>
public sealed class EgressRegistry : IEgressRegistry
{
    private readonly List<EgressEntry> _entries;
    private readonly HashSet<string> _allowedHosts;

    public EgressRegistry() : this(GetSeedEntries())
    {
    }

    public EgressRegistry(IEnumerable<EgressEntry> entries)
    {
        _entries = entries.ToList();
        _allowedHosts = new HashSet<string>(
            _entries.Select(e => NormalizeHost(e.Host)),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<EgressEntry> GetAllEntries() => _entries.AsReadOnly();

    public bool IsHostAllowed(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return _allowedHosts.Contains(NormalizeHost(host));
    }

    /// <summary>
    /// Registers an additional egress entry at runtime (e.g., when webhook
    /// subscriptions are created or connector credentials are configured).
    /// </summary>
    public void Register(EgressEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
        _allowedHosts.Add(NormalizeHost(entry.Host));
    }

    private static string NormalizeHost(string host)
    {
        return host.Trim().TrimEnd('.').ToLowerInvariant();
    }

    /// <summary>
    /// Returns the seed set of known outbound paths covering all LLM providers,
    /// webhook infrastructure, analytics, and error reporting destinations.
    /// </summary>
    private static List<EgressEntry> GetSeedEntries()
    {
        return
        [
            // OpenAI LLM provider
            new EgressEntry(
                Host: "api.openai.com",
                PayloadCategory: "LLM prompt with board context and user input",
                ToolOrAgentName: "OpenAiLlmProvider",
                Classification: EgressDataClassification.UserContent),

            // Google Gemini LLM provider
            new EgressEntry(
                Host: "generativelanguage.googleapis.com",
                PayloadCategory: "LLM prompt with board context and user input",
                ToolOrAgentName: "GeminiLlmProvider",
                Classification: EgressDataClassification.UserContent),

            // Outbound webhooks (user-configured destinations)
            new EgressEntry(
                Host: "*.webhook.site",
                PayloadCategory: "Board event notifications (card created/moved/archived)",
                ToolOrAgentName: "OutboundWebhookService",
                Classification: EgressDataClassification.MetadataOnly),

            // Sentry error reporting (when configured)
            new EgressEntry(
                Host: "*.ingest.sentry.io",
                PayloadCategory: "Error reports with stack traces and request metadata",
                ToolOrAgentName: "SentryIntegration",
                Classification: EgressDataClassification.MetadataOnly),

            // Self-hosted analytics script loading
            new EgressEntry(
                Host: "*.plausible.io",
                PayloadCategory: "Page view events (no user content)",
                ToolOrAgentName: "AnalyticsSettings",
                Classification: EgressDataClassification.MetadataOnly),
        ];
    }
}
