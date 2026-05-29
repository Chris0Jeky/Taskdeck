using System.Text.RegularExpressions;
using Xunit;

namespace Taskdeck.Architecture.Tests;

public class ApiControllerBoundaryTests
{
    private static readonly Regex ControllerDeclarationRegex = new(
        @"public\s+(?:abstract\s+)?(?:partial\s+)?class\s+(?<name>[A-Za-z0-9_]+)\s*:\s*(?<base>[A-Za-z0-9_:.]+)",
        RegexOptions.Compiled);

    private static readonly Regex AuthorizeAttributeTokenRegex = new(
        @"\bAuthorize(?:Attribute)?\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedControllerBaseTypes = new(StringComparer.Ordinal)
    {
        // AuthController uses per-method [Authorize] (mixed-auth: login/register are anonymous);
        // HealthController is intentionally anonymous (liveness/readiness probes). Both legitimately
        // sidestep class-level [Authorize] + the AuthenticatedControllerBase seam.
        "AuthController",
        "HealthController"
    };

    [Fact]
    public void Controllers_ShouldOnlyUseControllerBaseDirectly_WhenExplicitlyAllowed()
    {
        var violations = new List<string>();

        foreach (var controllerFile in GetControllerFiles())
        {
            var content = File.ReadAllText(controllerFile);
            var declaration = FindControllerDeclaration(controllerFile, content);

            if (!IsControllerBaseType(declaration.BaseType))
            {
                continue;
            }

            if (AllowedControllerBaseTypes.Contains(declaration.ClassName))
            {
                continue;
            }

            violations.Add(
                $"{ArchitectureTestPaths.ToBackendRelativePath(controllerFile)} declares {declaration.ClassName} : ControllerBase.");
        }

        Assert.True(
            violations.Count == 0,
            $"Unexpected direct ControllerBase inheritance detected:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void ProtectedControllers_ShouldDeclareAuthorizeAttribute()
    {
        var violations = new List<string>();

        foreach (var controllerFile in GetControllerFiles())
        {
            var content = File.ReadAllText(controllerFile);
            var declaration = FindControllerDeclaration(controllerFile, content);

            if (AllowedControllerBaseTypes.Contains(declaration.ClassName))
            {
                continue;
            }

            if (declaration.HasClassAuthorizeAttribute)
            {
                continue;
            }

            violations.Add($"{ArchitectureTestPaths.ToBackendRelativePath(controllerFile)} is missing [Authorize] on controller class.");
        }

        Assert.True(
            violations.Count == 0,
            $"Controllers inheriting AuthenticatedControllerBase must declare [Authorize]:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Actions_InControllersWithoutClassAuthorize_MustDeclareExplicitAuthorization()
    {
        // For controllers that do NOT carry a class-level [Authorize] (the mixed-auth
        // AuthController and the anonymous HealthController), every HTTP action must
        // state its intent explicitly via [Authorize] or [AllowAnonymous]. This stops a
        // newly-added action from being silently anonymous by omission.
        var violations = new List<string>();

        foreach (var controllerFile in GetControllerFiles())
        {
            var content = File.ReadAllText(controllerFile);
            var declaration = FindControllerDeclaration(controllerFile, content);

            // Controllers with a class-level [Authorize] cover all their actions by default.
            if (declaration.HasClassAuthorizeAttribute)
            {
                continue;
            }

            foreach (var action in GetActionsMissingExplicitAuthorization(content))
            {
                violations.Add(
                    $"{ArchitectureTestPaths.ToBackendRelativePath(controllerFile)}: action '{action}' is missing [Authorize] or [AllowAnonymous] (its controller has no class-level [Authorize]).");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Actions in controllers without class-level [Authorize] must declare explicit authorization:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IEnumerable<string> GetActionsMissingExplicitAuthorization(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var methodMatch = ActionMethodRegex.Match(lines[i]);
            if (!methodMatch.Success)
            {
                continue;
            }

            // Collect the attribute lines immediately preceding the method. Blank
            // lines are tolerated (skipped) so an IDE-inserted gap between an
            // attribute and the signature cannot truncate the block and let an
            // action slip past the guard — mirroring HasAuthorizeAttributeOnClass.
            var attributeLines = new List<string>();
            var j = i - 1;
            while (j >= 0)
            {
                var trimmed = lines[j].TrimStart();
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    attributeLines.Add(lines[j]);
                    j--;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    j--;
                    continue;
                }

                break;
            }

            var block = string.Join("\n", attributeLines);
            if (!HttpVerbAttributeRegex.IsMatch(block))
            {
                // Not an HTTP action method (e.g. a constructor or helper).
                continue;
            }

            if (!ExplicitAuthorizationRegex.IsMatch(block))
            {
                yield return methodMatch.Groups["method"].Value;
            }
        }
    }

    private static readonly Regex ActionMethodRegex = new(
        @"^\s*public\s+(?:async\s+)?[\w<>\[\],\.\?\s]+?\s+(?<method>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex HttpVerbAttributeRegex = new(
        @"\[Http(Get|Post|Put|Delete|Patch|Head|Options)(?:Attribute)?\b",
        RegexOptions.Compiled);

    // Accepts both the short and long attribute forms (e.g. [Authorize] and
    // [AuthorizeAttribute]), matching AuthorizeAttributeTokenRegex's behavior.
    private static readonly Regex ExplicitAuthorizationRegex = new(
        @"\[(Authorize|AllowAnonymous)(?:Attribute)?\b",
        RegexOptions.Compiled);

    private static IEnumerable<string> GetControllerFiles()
    {
        var controllersDirectory = ArchitectureTestPaths.GetBackendPath("src/Taskdeck.Api/Controllers");
        return Directory.GetFiles(controllersDirectory, "*Controller.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static ControllerDeclaration FindControllerDeclaration(string controllerFile, string content)
    {
        var match = ControllerDeclarationRegex.Match(content);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Unable to parse controller declaration in {ArchitectureTestPaths.ToBackendRelativePath(controllerFile)}.");
        }

        return new ControllerDeclaration(
            match.Groups["name"].Value,
            match.Groups["base"].Value,
            HasAuthorizeAttributeOnClass(content, match.Index));
    }

    private static bool IsControllerBaseType(string baseType)
    {
        return baseType.Equals("ControllerBase", StringComparison.Ordinal) ||
               baseType.EndsWith(".ControllerBase", StringComparison.Ordinal) ||
               baseType.EndsWith("::ControllerBase", StringComparison.Ordinal);
    }

    private static bool HasAuthorizeAttributeOnClass(string content, int classDeclarationIndex)
    {
        var linesBeforeClass = content[..classDeclarationIndex]
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        var index = linesBeforeClass.Length - 1;
        while (index >= 0 && string.IsNullOrWhiteSpace(linesBeforeClass[index]))
        {
            index--;
        }

        var attributeLines = new List<string>();
        while (index >= 0)
        {
            var line = linesBeforeClass[index];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                attributeLines.Add(line);
                index--;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                index--;
                continue;
            }

            break;
        }

        if (attributeLines.Count == 0)
        {
            return false;
        }

        attributeLines.Reverse();
        var classAttributeBlock = string.Join(Environment.NewLine, attributeLines);
        return AuthorizeAttributeTokenRegex.IsMatch(classAttributeBlock);
    }

    private sealed record ControllerDeclaration(string ClassName, string BaseType, bool HasClassAuthorizeAttribute);
}
