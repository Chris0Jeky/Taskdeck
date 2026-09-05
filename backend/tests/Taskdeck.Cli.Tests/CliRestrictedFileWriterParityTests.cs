using System.Text;
using Xunit;

namespace Taskdeck.Cli.Tests;

/// <summary>
/// Covers issue #2667 item 2: <c>Taskdeck.Cli.RestrictedFileWriter</c> is a deliberate copy of the
/// API-side lockdown helpers in <c>Taskdeck.Api.FirstRun.FirstRunBootstrapper</c> (the CLI cannot
/// reference the API project, and Taskdeck.Architecture.Tests enforces that). A copy silently drifts:
/// a fix applied to one side and not the other leaves the other writing secrets under the old, weaker
/// contract. This test reads both source files and asserts the shared method bodies are identical
/// once comments and whitespace are normalized away, so drift fails the build instead of shipping.
///
/// It compares BODIES, not comments: each side documents its own issue numbers and call sites.
/// </summary>
public class CliRestrictedFileWriterParityTests
{
    [Theory]
    [InlineData("WriteRestrictedFile(string)", "internal static void WriteRestrictedFile(string path, string contents)")]
    [InlineData("WriteRestrictedFile(byte[])", "internal static void WriteRestrictedFile(string path, byte[] contents)")]
    [InlineData("CreateRestrictedNewFile", "private static FileStream CreateRestrictedNewFile(string path)")]
    [InlineData("CreateOwnerOnlyFileWindows", "private static FileStream CreateOwnerOnlyFileWindows(string path)")]
    [InlineData("BuildOwnerOnlyFileSecurity", "private static FileSecurity BuildOwnerOnlyFileSecurity()")]
    [InlineData("RestrictFileToCurrentUser", "internal static void RestrictFileToCurrentUser(string path)")]
    public void SharedLockdownHelper_HasIdenticalBodiesOnBothSides(string method, string signature)
    {
        var apiSource = StripComments(File.ReadAllText(FindApiFirstRunBootstrapperSource()));
        var cliSource = StripComments(
            File.ReadAllText(CliRestrictedFileWriterTests.FindCliSourceFile("RestrictedFileWriter.cs")));

        var api = Normalize(ExtractBody(apiSource, signature, "Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs"));
        var cli = Normalize(ExtractBody(cliSource, signature, "Taskdeck.Cli/RestrictedFileWriter.cs"));

        if (api != cli)
        {
            Assert.Fail(DescribeDrift(method, api, cli));
        }
    }

    private static string DescribeDrift(string method, string api, string cli)
    {
        var apiLines = api.Split('\n');
        var cliLines = cli.Split('\n');
        var max = Math.Max(apiLines.Length, cliLines.Length);
        for (var i = 0; i < max; i++)
        {
            var apiLine = i < apiLines.Length ? apiLines[i] : "<end of method>";
            var cliLine = i < cliLines.Length ? cliLines[i] : "<end of method>";
            if (apiLine != cliLine)
            {
                return
                    $"{method} has drifted between the API original and the CLI copy. " +
                    $"First difference at normalized line {i + 1}:{Environment.NewLine}" +
                    $"  API (backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs): {apiLine}{Environment.NewLine}" +
                    $"  CLI (backend/src/Taskdeck.Cli/RestrictedFileWriter.cs):          {cliLine}{Environment.NewLine}" +
                    "Apply the change to both copies (see the RestrictedFileWriter class comment).";
            }
        }

        return $"{method} has drifted between the API original and the CLI copy.";
    }

    /// <summary>
    /// Walks up from the test output directory to the API source file. Throws (the test FAILS, it does
    /// not skip) when the source tree is not present: a parity test that cannot read the sources proves
    /// nothing, so it must not pass quietly.
    /// </summary>
    private static string FindApiFirstRunBootstrapperSource()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir, "backend", "src", "Taskdeck.Api", "FirstRun", "FirstRunBootstrapper.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException(
            "Could not locate backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs by walking up from " +
            AppContext.BaseDirectory);
    }

    /// <summary>
    /// Returns the method body (the brace block, or the <c>=&gt; ...;</c> expression) that follows
    /// <paramref name="signature"/>. Throws when the signature or its body cannot be located, so a
    /// renamed or restructured method fails loudly instead of comparing nothing.
    /// </summary>
    private static string ExtractBody(string source, string signature, string file)
    {
        var index = source.IndexOf(signature, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Could not find '{signature}' in {file}; the parity test cannot compare a method it cannot find.");
        }

        var i = index + signature.Length;
        while (i < source.Length && char.IsWhiteSpace(source[i]))
        {
            i++;
        }

        if (i + 1 < source.Length && source[i] == '=' && source[i + 1] == '>')
        {
            return source[i..FindStatementEnd(source, i, file, signature)];
        }

        if (i >= source.Length || source[i] != '{')
        {
            throw new InvalidOperationException(
                $"'{signature}' in {file} is followed by neither a block body nor an expression body.");
        }

        return source[i..FindBlockEnd(source, i, file, signature)];
    }

    private static int FindBlockEnd(string source, int openBrace, string file, string signature)
    {
        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            var c = source[i];
            if (c is '"' or '\'')
            {
                i = SkipLiteral(source, i);
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i + 1;
                }
            }
        }

        throw new InvalidOperationException(
            $"Unbalanced braces after '{signature}' in {file}.");
    }

    private static int FindStatementEnd(string source, int start, string file, string signature)
    {
        for (var i = start; i < source.Length; i++)
        {
            var c = source[i];
            if (c is '"' or '\'')
            {
                i = SkipLiteral(source, i);
                continue;
            }

            if (c == ';')
            {
                return i + 1;
            }
        }

        throw new InvalidOperationException(
            $"Unterminated expression body after '{signature}' in {file}.");
    }

    /// <summary>
    /// Returns the index of the closing quote of the literal that starts at <paramref name="start"/>,
    /// honoring backslash escapes and the <c>@"..."</c> doubled-quote form.
    /// </summary>
    private static int SkipLiteral(string source, int start)
    {
        var quote = source[start];
        var verbatim = quote == '"' && start > 0 && source[start - 1] == '@';
        for (var i = start + 1; i < source.Length; i++)
        {
            if (!verbatim && source[i] == '\\')
            {
                i++;
                continue;
            }

            if (source[i] != quote)
            {
                continue;
            }

            if (verbatim && i + 1 < source.Length && source[i + 1] == quote)
            {
                i++;
                continue;
            }

            return i;
        }

        return source.Length - 1;
    }

    /// <summary>
    /// Removes <c>//</c>, <c>///</c> and <c>/* */</c> comments while leaving string and char literals
    /// (and the braces inside interpolated strings) intact, so the comparison is over code only.
    /// </summary>
    private static string StripComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    i++;
                }

                i = Math.Min(i + 2, source.Length);
                continue;
            }

            if (c is '"' or '\'')
            {
                var end = SkipLiteral(source, i);
                sb.Append(source, i, end - i + 1);
                i = end + 1;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Trims each line, drops blank lines and collapses runs of whitespace, so indentation and line
    /// wrapping differences between the two copies do not register as drift.
    /// </summary>
    private static string Normalize(string body)
    {
        var lines = body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(CollapseWhitespace)
            .Where(line => line.Length > 0);

        return string.Join('\n', lines);
    }

    private static string CollapseWhitespace(string line)
    {
        var sb = new StringBuilder(line.Length);
        var previousWasSpace = false;
        foreach (var c in line.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    sb.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            previousWasSpace = false;
            sb.Append(c);
        }

        return sb.ToString();
    }
}
