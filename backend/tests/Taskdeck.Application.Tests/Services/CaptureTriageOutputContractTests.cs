using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CaptureTriageOutputContractTests
{
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
            .Be("^(?!\\s)(?!.*\\s$)(?!.*[\\u0000-\\u001F\\u007F-\\u009F\\u061C\\u200E\\u200F\\u2028\\u2029\\u202A-\\u202E\\u2066-\\u2069]).+$");
        AssertBoundedString(
            taskProperties.GetProperty("evidence"),
            CaptureTriageOutputContract.MaxTaskEvidenceLength);
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
