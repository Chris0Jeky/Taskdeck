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
    public void Validate_ShouldFail_WhenPromptVersionIsUnknown()
    {
        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            "llm-triage.v2",
            new[] { new CaptureTriageTaskV1("Follow up with QA", "I will follow up with QA tomorrow") });

        var result = CaptureTriageOutputContract.Validate(output);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("prompt version");
        result.ErrorMessage.Should().Contain(CaptureTriageOutputContract.PromptVersionV1);
        result.ErrorMessage.Should().Contain(CaptureTriageOutputContract.PromptVersionLlmV1);
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
