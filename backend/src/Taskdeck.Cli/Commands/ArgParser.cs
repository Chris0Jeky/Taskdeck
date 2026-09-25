namespace Taskdeck.Cli.Commands;

internal static class ArgParser
{
    public static bool HasFlag(IReadOnlyList<string> args, string optionName)
    {
        return args.Any(arg => string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase));
    }

    public static string? FindDuplicateOption(IReadOnlyList<string> args, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            var occurrences = 0;
            foreach (var arg in args)
            {
                if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
                {
                    occurrences++;
                    if (occurrences > 1)
                    {
                        return optionName;
                    }
                }
            }
        }

        return null;
    }

    public static string? GetOption(IReadOnlyList<string> args, string optionName)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                var optionValue = args[i + 1];
                if (optionValue.StartsWith("--", StringComparison.Ordinal))
                {
                    return null;
                }

                return optionValue;
            }
        }

        return null;
    }

    public static bool TryParseGuid(string? text, out Guid value)
    {
        return Guid.TryParse(text, out value);
    }

    public static string[] StripFlag(string[] args, string flag)
    {
        return args
            .Where(arg => !string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
