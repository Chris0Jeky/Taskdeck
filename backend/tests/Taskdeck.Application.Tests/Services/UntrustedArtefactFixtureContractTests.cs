using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class UntrustedArtefactFixtureContractTests
{
    private const int MaximumFixtureBytes = 16 * 1024;

    [Fact]
    public void Manifest_ShouldDeclareThreeUniqueUntrustedSourceKinds()
    {
        using var manifest = ReadManifest();
        var root = manifest.RootElement;

        root.GetProperty("version").GetInt32().Should().Be(1);
        var cases = root.GetProperty("sourceCases").EnumerateArray().ToList();
        cases.Select(item => item.GetProperty("id").GetString()).Should().OnlyHaveUniqueItems();
        cases.Select(item => item.GetProperty("sourceKind").GetString()).Should().BeEquivalentTo(
            "transcript",
            "pdf-extracted-text",
            "image-extracted-text");
    }

    [Fact]
    public void SourceFixtures_ShouldBeBoundedUtf8AndContainTheirStableCanaries()
    {
        using var manifest = ReadManifest();
        foreach (var fixtureCase in manifest.RootElement.GetProperty("sourceCases").EnumerateArray())
        {
            var fileName = fixtureCase.GetProperty("file").GetString()!;
            var canary = fixtureCase.GetProperty("canary").GetString()!;
            var bytes = File.ReadAllBytes(FixturePath(fileName));

            bytes.Should().NotBeEmpty();
            bytes.Length.Should().BeLessThan(MaximumFixtureBytes);
            var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            content.Should().Contain(canary);
            content.Should().ContainEquivalentOf("ignore previous instructions");
            fixtureCase.GetProperty("allowedVerdicts").GetArrayLength().Should().BeGreaterThan(0);
            fixtureCase.GetProperty("forbiddenOutcomes").GetArrayLength().Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void ResponseFixtures_ShouldDeclareFailClosedDispositionAndPromisedFormat()
    {
        using var manifest = ReadManifest();
        foreach (var responseCase in manifest.RootElement.GetProperty("responseCases").EnumerateArray())
        {
            responseCase.GetProperty("expectedDisposition").GetString()
                .Should().Be("deterministic-fallback");
            var content = File.ReadAllText(FixturePath(responseCase.GetProperty("file").GetString()!));
            var format = responseCase.GetProperty("format").GetString();

            if (format == "json")
            {
                var parse = () => JsonDocument.Parse(content);
                using var parsed = parse.Should().NotThrow().Which;
                parsed.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            }
            else
            {
                var parse = () => JsonDocument.Parse(content);
                parse.Should().Throw<JsonException>();
            }
        }
    }

    private static JsonDocument ReadManifest()
        => JsonDocument.Parse(File.ReadAllText(FixturePath("manifest.json")));

    private static string FixturePath(string fileName)
        => Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "tests",
            "Taskdeck.Application.Tests",
            "Fixtures",
            "untrusted-artefacts",
            fileName);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitDirectory = Path.Combine(directory.FullName, ".git");
            var solutionPath = Path.Combine(directory.FullName, "backend", "Taskdeck.sln");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory) || File.Exists(solutionPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test runtime directory.");
    }
}
