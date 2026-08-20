using System.Reflection;

namespace Taskdeck.Application.Common;

/// <summary>
/// The product version stamped into the backend assemblies at build time.
/// </summary>
/// <remarks>
/// Release workflows inject the version derived from the <c>v*</c> release tag
/// (<c>/p:Version=&lt;tag without the leading v&gt;</c>). Builds that inject nothing —
/// developer machines, ordinary CI, a local <c>docker build</c> — fall back to
/// <see cref="DevelopmentFallback"/> through <c>backend/Directory.Build.props</c>, so an
/// unstamped build reports something obviously unreleased rather than a plausible number.
/// <para>
/// That single props file stamps every backend project, so this assembly's informational
/// version answers "what version am I running?" identically for the API, the MCP server,
/// and the CLI.
/// </para>
/// </remarks>
public static class ProductVersion
{
    /// <summary>Version reported when no release version was injected at build time.</summary>
    public const string DevelopmentFallback = "0.0.0-dev";

    private static readonly string ResolvedVersion = Resolve(typeof(ProductVersion).Assembly);

    /// <summary>The stamped product version. Never null, empty, or whitespace.</summary>
    public static string Value => ResolvedVersion;

    /// <summary>
    /// Reads the informational version from <paramref name="assembly"/>, falling back to
    /// <see cref="DevelopmentFallback"/> when the attribute is absent or blank.
    /// </summary>
    internal static string Resolve(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Normalize(assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);
    }

    /// <summary>
    /// Normalizes a raw informational version: strips any <c>+&lt;build metadata&gt;</c> suffix so
    /// the reported value stays directly comparable with the release tag the build was cut from,
    /// and falls back to <see cref="DevelopmentFallback"/> for a missing or blank value.
    /// </summary>
    internal static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return DevelopmentFallback;
        }

        var metadataIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        var trimmed = (metadataIndex >= 0
                ? informationalVersion[..metadataIndex]
                : informationalVersion)
            .Trim();

        return trimmed.Length == 0 ? DevelopmentFallback : trimmed;
    }
}
