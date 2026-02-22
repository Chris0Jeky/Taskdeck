namespace Taskdeck.Architecture.Tests;

internal static class ArchitectureTestPaths
{
    private static readonly Lazy<string> BackendRootLazy = new(ResolveBackendRoot);

    public static string BackendRoot => BackendRootLazy.Value;

    public static string GetBackendPath(string relativePath)
    {
        return Path.Combine(
            BackendRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string ToBackendRelativePath(string absolutePath)
    {
        var normalizedRoot = Path.GetFullPath(BackendRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(absolutePath);

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return absolutePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ResolveBackendRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "backend", "Taskdeck.sln");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate backend/Taskdeck.sln from test execution directory.");
    }
}
