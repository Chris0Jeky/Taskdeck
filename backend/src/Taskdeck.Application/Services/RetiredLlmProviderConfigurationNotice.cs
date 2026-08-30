namespace Taskdeck.Application.Services;

/// <summary>
/// Records retired provider configuration that a packaged desktop start inherited from the
/// process environment and deliberately ignored (#2233). The notice carries configuration
/// KEY NAMES only — never values — so it can be logged and surfaced in the provider-status
/// surface without disclosing a leftover key or endpoint.
/// <para>
/// An empty notice is the normal state: it is registered unconditionally so consumers never
/// have to branch on hosting mode, and only the packaged desktop host ever populates it.
/// </para>
/// </summary>
public sealed class RetiredLlmProviderConfigurationNotice
{
    private readonly object _gate = new();
    private readonly SortedSet<string> _ignoredKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True once at least one retired environment-sourced key was ignored.</summary>
    public bool IgnoredEnvironmentConfiguration
    {
        get
        {
            lock (_gate)
            {
                return _ignoredKeys.Count > 0;
            }
        }
    }

    /// <summary>The ignored configuration key names, ordered and de-duplicated. Never values.</summary>
    public IReadOnlyList<string> IgnoredKeys
    {
        get
        {
            lock (_gate)
            {
                return _ignoredKeys.ToArray();
            }
        }
    }

    /// <summary>
    /// Records one ignored configuration key. Repeated recordings of the same key collapse,
    /// because a configuration reload rebuilds every provider and replays its dropped keys.
    /// </summary>
    public void RecordIgnoredKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            _ignoredKeys.Add(key);
        }
    }
}
