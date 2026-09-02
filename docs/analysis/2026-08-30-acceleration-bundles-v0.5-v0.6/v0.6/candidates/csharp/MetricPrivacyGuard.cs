
namespace Taskdeck.Acceleration.V06;

public static class MetricPrivacyGuard
{
    private static readonly string[] ForbiddenNameFragments =
    {
        "text", "prompt", "quote", "transcript", "filename", "fileName",
        "url", "message", "description", "title", "content", "sourceBytes", "speakerName"
    };

    public static IReadOnlyList<string> ValidateFieldNames(IEnumerable<string> names) =>
        names.Where(name => ForbiddenNameFragments.Any(fragment =>
                name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .Select(name => $"metrics.forbidden-field:{name}")
            .ToList();

    public static bool LooksContentBearing(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length > 128) return true;
        if (value.Contains('\n') || value.Contains('\r')) return true;
        return false;
    }
}
