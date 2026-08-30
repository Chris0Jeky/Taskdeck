namespace Taskdeck.Application.Services;

/// <summary>
/// Concrete egress registry seeded with all known outbound paths.
/// Implements GP-10: every external destination must be disclosed/enforced.
///
/// Seed entries cover:
/// - OpenAI API (LLM provider)
/// - Configured outbound webhook destinations
/// - Self-hosted analytics (Plausible/Umami)
/// - Sentry error reporting
///
/// Additional entries can be registered at startup when new connectors or
/// providers are configured.
/// </summary>
public sealed class EgressRegistry : IEgressRegistry
{
    private readonly object _lock = new();
    private readonly List<EgressEntry> _entries;
    private readonly HashSet<string> _exactHosts;
    private readonly List<string> _wildcardSuffixes;

    public EgressRegistry() : this(GetSeedEntries())
    {
    }

    internal EgressRegistry(IEnumerable<EgressEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = new List<EgressEntry>();
        _exactHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _wildcardSuffixes = new List<string>();

        foreach (var entry in entries)
        {
            ValidateEntry(entry);
            _entries.Add(entry);
            ClassifyHost(NormalizeHost(entry.Host));
        }
    }

    public IReadOnlyList<EgressEntry> GetAllEntries()
    {
        lock (_lock)
        {
            return _entries.ToList().AsReadOnly();
        }
    }

    public bool IsHostAllowed(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalized = NormalizeHost(host);

        lock (_lock)
        {
            if (_exactHosts.Contains(normalized))
            {
                return true;
            }

            foreach (var suffix in _wildcardSuffixes)
            {
                if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Registers an additional egress entry at runtime (e.g., when webhook
    /// subscriptions are created or connector credentials are configured).
    /// Thread-safe: protected by a lock over _entries, _exactHosts, and _wildcardSuffixes.
    /// </summary>
    public void Register(EgressEntry entry)
    {
        ValidateEntry(entry);

        lock (_lock)
        {
            _entries.Add(entry);
            ClassifyHost(NormalizeHost(entry.Host));
        }
    }

    private static void ValidateEntry(EgressEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Host))
        {
            throw new ArgumentException("Host cannot be null or whitespace.", nameof(entry));
        }

        var normalized = NormalizeHost(entry.Host);
        if (normalized.Contains("://", StringComparison.Ordinal) ||
            normalized.Contains('/', StringComparison.Ordinal) ||
            normalized.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("Host must be a DNS host name, not a URL or path.", nameof(entry));
        }

        if (normalized.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = normalized[2..];
            var labels = suffix.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (labels.Length < 2 ||
                labels.Any(static label => label.Length == 0) ||
                suffix.Contains('*', StringComparison.Ordinal) ||
                Uri.CheckHostName(suffix) != UriHostNameType.Dns)
            {
                throw new ArgumentException("Wildcard host must target a concrete multi-label DNS suffix.", nameof(entry));
            }

            return;
        }

        if (normalized.Contains('*', StringComparison.Ordinal) ||
            Uri.CheckHostName(normalized) != UriHostNameType.Dns)
        {
            throw new ArgumentException("Host must be a valid DNS host name.", nameof(entry));
        }
    }

    /// <summary>
    /// Classifies a normalized host as either an exact match or a wildcard suffix.
    /// Wildcard patterns start with "*." and match any subdomain.
    /// </summary>
    private void ClassifyHost(string normalized)
    {
        if (normalized.StartsWith("*.", StringComparison.Ordinal))
        {
            // Store the suffix (e.g., "*.webhook.site" -> ".webhook.site")
            _wildcardSuffixes.Add(normalized[1..]);
        }
        else
        {
            _exactHosts.Add(normalized);
        }
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

            // Ollama local LLM provider (localhost — no data leaves the machine)
            new EgressEntry(
                Host: "localhost",
                PayloadCategory: "LLM prompt with board context and user input (local inference)",
                ToolOrAgentName: "OllamaLlmProvider",
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

            // GitHub connector (issue sync, PR references)
            new EgressEntry(
                Host: "api.github.com",
                PayloadCategory: "Repository metadata and issue content",
                ToolOrAgentName: "GitHubConnectorProvider",
                Classification: EgressDataClassification.UserContent),
        ];
    }
}
