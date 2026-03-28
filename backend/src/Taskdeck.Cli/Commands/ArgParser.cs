namespace Taskdeck.Cli.Commands;

internal static class ArgParser
{
    public static bool HasFlag(IReadOnlyList<string> args, string optionName)
    {
        return args.Any(arg => string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase));
    }

    public static string? GetOption(IReadOnlyList<string> args, string optionName)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
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
