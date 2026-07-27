using System.Text;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CaptureTriageOutputContractTests
{
    private const string ExpectedLlmV2TitlePattern =
        "^(?!\\s)(?!.*\\s$)(?!.*[\\u0000-\\u001F\\u007F-\\u009F\\u061C\\u200E\\u200F\\u2028\\u2029\\u202A-\\u202E\\u2066-\\u2069\\uFEFF]).+$";
    private const string ExpectedLlmV2EvidencePattern = "[^\\s\\uFEFF]";

    [Fact]
    public void ParseAndValidate_ShouldPass_ForGoldenFixture()
    {
        var json = ReadFixture("valid.v1.json");

        var result = CaptureTriageOutputContract.ParseAndValidate(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(CaptureTriageOutputContract.SchemaVersion);
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionV1);
        result.Value.Tasks.Should().HaveCount(2);
    }

    [Fact]
    public void ParseAndValidate_ShouldFail_WhenTasksAreMissing()
    {
        var json = ReadFixture("invalid.missing-tasks.json");

        var result = CaptureTriageOutputContract.ParseAndValidate(json);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("at least one task");
    }

    [Fact]
    public void ParseAndValidate_ShouldFail_WhenPromptVersionIsUnexpected()
    {
        var json = ReadFixture("invalid.prompt-version.json");

        var result = CaptureTriageOutputContract.ParseAndValidate(json);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("prompt version");
    }

    [Fact]
    public void ParseAndValidate_ShouldFail_WhenUnknownPropertyIsPresent()
    {
        var json = ReadFixture("invalid.additional-property.json");

        var result = CaptureTriageOutputContract.ParseAndValidate(json);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("JSON is invalid");
    }

    [Fact]
    public void ParseAndValidate_ShouldFail_WhenTaskElementIsNull()
    {
        var json = ReadFixture("invalid.null-task.json");

        var result = CaptureTriageOutputContract.ParseAndValidate(json);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("cannot be null");
    }

    [Fact]
    public void ParseAndValidate_ShouldPass_ForLlmGoldenFixture()
    {
        var json = ReadFixture("valid.llm-v1.json");

        var result = CaptureTriageOutputContract.ParseAndValidate(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(CaptureTriageOutputContract.SchemaVersion);
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV1);
        result.Value.Tasks.Should().HaveCount(2);
    }

    [Fact]
    public void ParseAndValidate_ShouldPass_ForLlmV2GoldenFixture()
    {
        var json = ReadFixture("valid.llm-v2.json");

        var result = CaptureTriageOutputContract.ParseAndValidate(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(CaptureTriageOutputContract.SchemaVersion);
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
        result.Value.Tasks.Should().HaveCount(2);
    }

    [Fact]
    public void Validate_ShouldPass_WhenPromptVersionIsLlmV1()
    {
        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionLlmV1,
            new[] { new CaptureTriageTaskV1("Follow up with QA", "I will follow up with QA tomorrow") });

        var result = CaptureTriageOutputContract.Validate(output);

        result.IsSuccess.Should().BeTrue();
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV1);
    }

    [Fact]
    public void Validate_ShouldPass_WhenPromptVersionIsLlmV2()
    {
        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            new[] { new CaptureTriageTaskV1("Follow up with QA", "I will follow up with QA tomorrow") });

        var result = CaptureTriageOutputContract.Validate(output);

        result.IsSuccess.Should().BeTrue();
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
    }

    [Fact]
    public void Validate_ShouldFail_WhenPromptVersionIsUnknown()
    {
        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            "llm-triage.v3",
            new[] { new CaptureTriageTaskV1("Follow up with QA", "I will follow up with QA tomorrow") });

        var result = CaptureTriageOutputContract.Validate(output);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("prompt version");
        result.ErrorMessage.Should().Contain(CaptureTriageOutputContract.PromptVersionV1);
        result.ErrorMessage.Should().Contain(CaptureTriageOutputContract.PromptVersionLlmV1);
        result.ErrorMessage.Should().Contain(CaptureTriageOutputContract.PromptVersionLlmV2);
    }

    [Theory]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("line\nbreak")]
    [InlineData("c1\u0085control")]
    [InlineData("bidi\u202Eoverride")]
    [InlineData("isolate\u2066text")]
    [InlineData("\uFEFFleading bom")]
    [InlineData("trailing bom\uFEFF")]
    [InlineData("embedded\uFEFFbom")]
    public void Validate_ShouldFail_WhenTaskTitleContainsUnsafeWhitespaceControlOrBidi(string title)
    {
        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            [new CaptureTriageTaskV1(title, "source evidence")]);

        var result = CaptureTriageOutputContract.Validate(output);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("unsafe");
    }

    [Theory]
    [InlineData(CaptureTriageOutputContract.PromptVersionV1)]
    [InlineData(CaptureTriageOutputContract.PromptVersionLlmV1)]
    public void Validate_ShouldPreserveLegacyV1TitleSemantics(string promptVersion)
    {
        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            promptVersion,
            [new CaptureTriageTaskV1("legacy\u202Etitle", "source evidence")]);

        var result = CaptureTriageOutputContract.Validate(output);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(CaptureTriageOutputContract.PromptVersionV1)]
    [InlineData(CaptureTriageOutputContract.PromptVersionLlmV1)]
    public void Validate_ShouldPreserveLegacyUtf16LengthLimits(string promptVersion)
    {
        var exactlyAtLimit = string.Concat(Enumerable.Repeat("😀", 90));
        var overLimit = string.Concat(Enumerable.Repeat("😀", 91));

        CaptureTriageOutputContract.Validate(new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            promptVersion,
            [new CaptureTriageTaskV1(exactlyAtLimit, "source evidence")])).IsSuccess.Should().BeTrue();
        CaptureTriageOutputContract.Validate(new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            promptVersion,
            [new CaptureTriageTaskV1(overLimit, "source evidence")])).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldUseUnicodeScalarLimitsForLlmV2()
    {
        var hundredEmoji = string.Concat(Enumerable.Repeat("😀", 100));
        var maximumTitle = string.Concat(Enumerable.Repeat("😀", 180));
        var overlongTitle = string.Concat(Enumerable.Repeat("😀", 181));
        var maximumEvidence = string.Concat(Enumerable.Repeat("😀", 280));
        var overlongEvidence = string.Concat(Enumerable.Repeat("😀", 281));

        ValidateLlmV2Task(hundredEmoji, "source evidence").IsSuccess.Should().BeTrue();
        ValidateLlmV2Task(maximumTitle, "source evidence").IsSuccess.Should().BeTrue();
        ValidateLlmV2Task(overlongTitle, "source evidence").IsSuccess.Should().BeFalse();
        ValidateLlmV2Task("Send report", maximumEvidence).IsSuccess.Should().BeTrue();
        ValidateLlmV2Task("Send report", overlongEvidence).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void TriageSchemaFile_ShouldDeclarePromptVersionAndStrictness()
    {
        var schemaPath = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "src",
            "Taskdeck.Application",
            "Schemas",
            "capture-triage-output.v1.schema.json");

        File.Exists(schemaPath).Should().BeTrue();
        var schema = File.ReadAllText(schemaPath);
        schema.Should().Contain("\"const\": \"triage.v1\"");
        schema.Should().Contain("\"additionalProperties\": false");
    }

    [Fact]
    public void LlmTriageSchemaFile_ShouldDeclarePromptVersionAndStrictness()
    {
        var schemaPath = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "src",
            "Taskdeck.Application",
            "Schemas",
            "capture-triage-output.llm-v1.schema.json");

        File.Exists(schemaPath).Should().BeTrue();
        var schema = File.ReadAllText(schemaPath);
        schema.Should().Contain("\"const\": \"llm-triage.v1\"");
        schema.Should().Contain("\"additionalProperties\": false");
    }

    [Fact]
    public void LlmV2TriageSchemaFile_ShouldDeclareFullContractStructureAndBounds()
    {
        var schemaPath = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "src",
            "Taskdeck.Application",
            "Schemas",
            "capture-triage-output.llm-v2.schema.json");

        File.Exists(schemaPath).Should().BeTrue();
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = document.RootElement;

        root.ValueKind.Should().Be(JsonValueKind.Object);
        root.GetProperty("$schema").GetString().Should()
            .Be("https://json-schema.org/draft/2020-12/schema");
        root.GetProperty("$id").GetString().Should()
            .Be("https://taskdeck.dev/schemas/capture-triage-output.llm-v2.schema.json");
        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        ReadRequiredNames(root).Should().BeEquivalentTo("version", "promptVersion", "tasks");

        var properties = root.GetProperty("properties");
        properties.EnumerateObject().Select(property => property.Name).Should()
            .BeEquivalentTo("version", "promptVersion", "tasks");

        var version = properties.GetProperty("version");
        version.GetProperty("type").GetString().Should().Be("integer");
        version.GetProperty("const").GetInt32().Should().Be(CaptureTriageOutputContract.SchemaVersion);

        var promptVersion = properties.GetProperty("promptVersion");
        promptVersion.GetProperty("type").GetString().Should().Be("string");
        promptVersion.GetProperty("const").GetString().Should()
            .Be(CaptureTriageOutputContract.PromptVersionLlmV2);

        var tasks = properties.GetProperty("tasks");
        tasks.GetProperty("type").GetString().Should().Be("array");
        tasks.GetProperty("minItems").GetInt32().Should().Be(1);
        tasks.GetProperty("maxItems").GetInt32().Should().Be(CaptureTriageOutputContract.MaxTasks);

        var task = tasks.GetProperty("items");
        task.GetProperty("type").GetString().Should().Be("object");
        task.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        ReadRequiredNames(task).Should().BeEquivalentTo("title", "evidence");

        var taskProperties = task.GetProperty("properties");
        taskProperties.EnumerateObject().Select(property => property.Name).Should()
            .BeEquivalentTo("title", "evidence");
        AssertBoundedString(
            taskProperties.GetProperty("title"),
            CaptureTriageOutputContract.MaxTaskTitleLength);
        taskProperties.GetProperty("title").GetProperty("pattern").GetString().Should()
            .Be(ExpectedLlmV2TitlePattern);
        AssertBoundedString(
            taskProperties.GetProperty("evidence"),
            CaptureTriageOutputContract.MaxTaskEvidenceLength);
        taskProperties.GetProperty("evidence").GetProperty("pattern").GetString().Should()
            .Be(ExpectedLlmV2EvidencePattern);
    }

    [Theory]
    [InlineData("Review the release", "source evidence", true)]
    [InlineData(" leading", "source evidence", false)]
    [InlineData("bidi\u202Eoverride", "source evidence", false)]
    [InlineData("\uFEFFleading", "source evidence", false)]
    [InlineData("trailing\uFEFF", "source evidence", false)]
    [InlineData("embedded\uFEFFbom", "source evidence", false)]
    [InlineData("Review the release", " ", false)]
    [InlineData("Review the release", "\t\r\n", false)]
    [InlineData("Review the release", "\uFEFF", false)]
    [InlineData("Review the release", " \uFEFF ", false)]
    [InlineData("Review the release", "\u0085", true)]
    [InlineData("Review the release", "\uFEFFsource", true)]
    [InlineData("Review the release", " source evidence ", true)]
    public void LlmV2SchemaAndRuntime_ShouldAgreeForStringConstraintExamples(
        string title,
        string evidence,
        bool expected)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetLlmV2SchemaPath()));
        var taskProperties = document.RootElement
            .GetProperty("properties")
            .GetProperty("tasks")
            .GetProperty("items")
            .GetProperty("properties");
        var schemaAccepts = MatchesStringSchema(taskProperties.GetProperty("title"), title) &&
                            MatchesStringSchema(taskProperties.GetProperty("evidence"), evidence);
        var runtime = CaptureTriageOutputContract.Validate(new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            [new CaptureTriageTaskV1(title, evidence)]));

        schemaAccepts.Should().Be(expected);
        runtime.IsSuccess.Should().Be(schemaAccepts);
    }

    [Fact]
    public void LlmV2SchemaAndRuntime_ShouldAgreeAtUnicodeScalarBoundaries()
    {
        var cases = new[]
        {
            (Title: string.Concat(Enumerable.Repeat("😀", 100)), Evidence: "source", Expected: true),
            (Title: string.Concat(Enumerable.Repeat("😀", 180)), Evidence: "source", Expected: true),
            (Title: string.Concat(Enumerable.Repeat("😀", 181)), Evidence: "source", Expected: false),
            (Title: "Send report", Evidence: string.Concat(Enumerable.Repeat("😀", 280)), Expected: true),
            (Title: "Send report", Evidence: string.Concat(Enumerable.Repeat("😀", 281)), Expected: false)
        };

        using var document = JsonDocument.Parse(File.ReadAllText(GetLlmV2SchemaPath()));
        var taskProperties = document.RootElement
            .GetProperty("properties")
            .GetProperty("tasks")
            .GetProperty("items")
            .GetProperty("properties");

        foreach (var testCase in cases)
        {
            var schemaAccepts = MatchesStringSchema(taskProperties.GetProperty("title"), testCase.Title) &&
                                MatchesStringSchema(taskProperties.GetProperty("evidence"), testCase.Evidence);
            var runtime = ValidateLlmV2Task(testCase.Title, testCase.Evidence);

            schemaAccepts.Should().Be(testCase.Expected);
            runtime.IsSuccess.Should().Be(schemaAccepts);
        }
    }

    private static string[] ReadRequiredNames(JsonElement schemaObject)
    {
        var required = schemaObject.GetProperty("required");
        required.ValueKind.Should().Be(JsonValueKind.Array);
        return required.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static void AssertBoundedString(JsonElement property, int maximumLength)
    {
        property.GetProperty("type").GetString().Should().Be("string");
        property.GetProperty("minLength").GetInt32().Should().Be(1);
        property.GetProperty("maxLength").GetInt32().Should().Be(maximumLength);
    }

    private static bool MatchesStringSchema(JsonElement property, string value)
    {
        var scalarLength = value.EnumerateRunes().Count();
        if (scalarLength < property.GetProperty("minLength").GetInt32() ||
            scalarLength > property.GetProperty("maxLength").GetInt32())
        {
            return false;
        }

        if (!property.TryGetProperty("pattern", out var pattern))
        {
            return true;
        }

        return pattern.GetString() switch
        {
            ExpectedLlmV2TitlePattern => MatchesLlmV2TitlePattern(value),
            ExpectedLlmV2EvidencePattern => value.EnumerateRunes().Any(rune => !IsEcmaWhitespace(rune.Value)),
            var unexpected => throw new InvalidOperationException($"Unexpected schema pattern: {unexpected}")
        };
    }

    private static bool MatchesLlmV2TitlePattern(string value)
    {
        var runes = value.EnumerateRunes().ToArray();
        if (runes.Length == 0 ||
            IsEcmaWhitespace(runes[0].Value) ||
            IsEcmaWhitespace(runes[^1].Value))
        {
            return false;
        }

        return runes.All(rune => !IsLlmV2TitleUnsafe(rune.Value));
    }

    private static bool IsLlmV2TitleUnsafe(int codePoint)
    {
        return codePoint is >= 0x0000 and <= 0x001F or
               >= 0x007F and <= 0x009F or
               0x061C or 0x200E or 0x200F or 0x2028 or 0x2029 or
               >= 0x202A and <= 0x202E or
               >= 0x2066 and <= 0x2069 or
               0xFEFF;
    }

    private static bool IsEcmaWhitespace(int codePoint)
    {
        return codePoint is >= 0x0009 and <= 0x000D or
               0x0020 or 0x00A0 or 0x1680 or
               >= 0x2000 and <= 0x200A or
               0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 or 0xFEFF;
    }

    private static Taskdeck.Domain.Common.Result<CaptureTriageOutputV1> ValidateLlmV2Task(
        string title,
        string evidence)
    {
        return CaptureTriageOutputContract.Validate(new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            [new CaptureTriageTaskV1(title, evidence)]));
    }

    private static string GetLlmV2SchemaPath()
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "src",
            "Taskdeck.Application",
            "Schemas",
            "capture-triage-output.llm-v2.schema.json");
    }

    private static string ReadFixture(string fixtureName)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "tests",
            "Taskdeck.Application.Tests",
            "Fixtures",
            "capture-triage-output",
            fixtureName);

        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitDirectory = Path.Combine(directory.FullName, ".git");
            var solutionPath = Path.Combine(directory.FullName, "backend", "Taskdeck.sln");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory) || File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test runtime directory.");
    }
}
