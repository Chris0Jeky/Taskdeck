using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.FirstRun;

/// <summary>
/// Packaged-desktop-only policy for retired provider configuration that arrives from the
/// PROCESS ENVIRONMENT (#2233).
/// <para>
/// A Windows profile that once ran a Gemini-era Taskdeck keeps user-scope
/// <c>Llm__Gemini__*</c> variables forever, and every later double-click inherits them. The
/// packaged app must still start, so the retired names are dropped from the environment
/// configuration source before anything reads them; selection then falls through to the
/// packaged default (Mock / deterministic triage).
/// </para>
/// <para>
/// The distinction is made at the configuration-SOURCE level: only
/// <c>EnvironmentVariablesConfigurationSource</c> instances are filtered. Retired
/// configuration the user wrote into Taskdeck's own <c>appsettings.json</c> /
/// <c>appsettings.local.json</c> is a deliberate choice and keeps failing loud, and non-desktop
/// hosts never call this at all, so the container / <c>dotnet run</c> / CI contract is unchanged.
/// </para>
/// </summary>
internal static class RetiredProviderEnvironmentConfiguration
{
    internal const string ProviderKey = "Llm:Provider";
    internal const string RetiredProviderValue = "Gemini";
    internal const string RetiredSectionKey = "Llm:Gemini";
    internal const string RetiredComposeMarkerKey =
        "TaskdeckMigration:RetiredLlmProviderConfigurationPresent";

    private static readonly string RetiredSectionPrefix =
        RetiredSectionKey + ConfigurationPath.KeyDelimiter;

    /// <summary>
    /// True when this environment-sourced configuration entry is retired provider configuration:
    /// the retired <c>Llm:Gemini</c> section (or any child of it), a <c>Llm:Provider</c> selector
    /// naming the retired provider, or the retired Docker Compose migration marker.
    /// A supported selector and every non-retired key are left untouched.
    /// </summary>
    internal static bool IsRetiredEntry(string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Equals(RetiredSectionKey, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(RetiredSectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (key.Equals(ProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            return value?.Trim().Equals(RetiredProviderValue, StringComparison.OrdinalIgnoreCase) == true;
        }

        return key.Equals(RetiredComposeMarkerKey, StringComparison.OrdinalIgnoreCase)
            && value?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Removes every retired entry from a loaded environment provider's data and records the
    /// dropped key names on <paramref name="notice"/>. Returns the dropped keys.
    /// </summary>
    internal static IReadOnlyList<string> RemoveRetiredEntries(
        IDictionary<string, string?> data,
        RetiredLlmProviderConfigurationNotice notice)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(notice);

        var retired = data
            .Where(entry => IsRetiredEntry(entry.Key, entry.Value))
            .Select(entry => entry.Key)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var key in retired)
        {
            data.Remove(key);
            notice.RecordIgnoredKey(key);
        }

        return retired;
    }

    /// <summary>
    /// Replaces every environment-variables source on <paramref name="builder"/> with a
    /// filtering equivalent that drops retired provider entries, preserving source order and
    /// each source's prefix so configuration precedence is unchanged.
    /// </summary>
    internal static void IgnoreInheritedRetiredProviderConfiguration(
        IConfigurationBuilder builder,
        RetiredLlmProviderConfigurationNotice notice)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(notice);

        var sources = builder.Sources;
        for (var index = 0; index < sources.Count; index++)
        {
            if (sources[index] is not EnvironmentVariablesConfigurationSource environmentSource)
            {
                continue;
            }

            // Replace in place: the index is the precedence slot, and a remove-then-append
            // would promote the environment above later JSON sources.
            sources[index] = new FilteredEnvironmentVariablesConfigurationSource(
                environmentSource.Prefix,
                notice);
        }
    }
}

/// <summary>
/// An environment-variables source whose provider drops retired provider entries after load.
/// It implements <see cref="IConfigurationSource"/> directly rather than deriving from
/// <c>EnvironmentVariablesConfigurationSource</c>, whose Build is not virtual, and so is
/// never mistaken for a framework source on a second pass.
/// </summary>
internal sealed class FilteredEnvironmentVariablesConfigurationSource : IConfigurationSource
{
    private readonly RetiredLlmProviderConfigurationNotice _notice;

    internal FilteredEnvironmentVariablesConfigurationSource(
        string? prefix,
        RetiredLlmProviderConfigurationNotice notice)
    {
        Prefix = prefix;
        _notice = notice ?? throw new ArgumentNullException(nameof(notice));
    }

    internal string? Prefix { get; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new FilteredEnvironmentVariablesConfigurationProvider(Prefix, _notice);
}

/// <summary>
/// Loads the process environment exactly as the framework provider does, then removes the
/// retired provider entries so no later reader — binding, section enumeration, or the
/// registration-time retired-configuration check — can observe them.
/// </summary>
internal sealed class FilteredEnvironmentVariablesConfigurationProvider
    : EnvironmentVariablesConfigurationProvider
{
    private readonly RetiredLlmProviderConfigurationNotice _notice;

    internal FilteredEnvironmentVariablesConfigurationProvider(
        string? prefix,
        RetiredLlmProviderConfigurationNotice notice)
        : base(prefix)
    {
        _notice = notice ?? throw new ArgumentNullException(nameof(notice));
    }

    public override void Load()
    {
        base.Load();
        RetiredProviderEnvironmentConfiguration.RemoveRetiredEntries(Data, _notice);
    }
}
