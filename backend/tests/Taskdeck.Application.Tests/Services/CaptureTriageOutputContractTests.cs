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
    public void ParseAndValidateV2_ShouldPass_ForLlmGoldenFixture()
    {
        var json = ReadFixture("valid.llm-v2.json");

        var result = CaptureTriageOutputContract.ParseAndValidateV2(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(CaptureTriageOutputContract.SchemaVersionV2);
        result.Value.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
        result.Value.Tasks.Should().HaveCount(2);
        result.Value.Tasks[0].Type.Should().Be("action");
        result.Value.Tasks[0].AssigneeHint.Should().Be("Alice");
        result.Value.Tasks[0].DueDateHint.Should().Be("2026-08-07");
        result.Value.Tasks[0].EvidenceQuote.Should().Contain("revised budget");
    }

    [Fact]
    public void ParseAndValidateV2_ShouldRejectMissingRequiredMetadata()
    {
        const string json = """
                            {
                              "version": 2,
                              "promptVersion": "llm-triage.v2",
                              "tasks": [
                                {
                                  "title": "Follow up with QA",
                                  "type": "action",
                                  "assigneeHint": null,
                                  "dueDateHint": null,
                                  "evidenceQuote": "I will follow up with QA tomorrow"
                                }
                              ]
                            }
                            """;

        var result = CaptureTriageOutputContract.ParseAndValidateV2(json);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("JSON is invalid");
    }

    [Fact]
    public void ValidateV2_ShouldRejectInvalidMetadataWithoutNormalizingIt()
    {
        var validTask = new CaptureTriageTaskV2(
            "Follow up with QA",
            "action",
            "Alice",
            "2026-08-07",
            0.9m,
            "Alice: I will follow up with QA tomorrow.");
        var invalidTasks = new[]
        {
            validTask with { Type = "Action" },
            validTask with { AssigneeHint = "   " },
            validTask with { DueDateHint = "next Friday" },
            validTask with { DueDateHint = "2026-02-30" },
            validTask with { Confidence = -0.01m },
            validTask with { Confidence = 1.01m },
            validTask with { EvidenceQuote = "  " }
        };

        foreach (var invalidTask in invalidTasks)
        {
            var result = CaptureTriageOutputContract.Validate(new CaptureTriageOutputV2(
                CaptureTriageOutputContract.SchemaVersionV2,
                CaptureTriageOutputContract.PromptVersionLlmV2,
                [invalidTask]));

            result.IsSuccess.Should().BeFalse(invalidTask.ToString());
        }
    }

    [Fact]
    public void ValidateV2_ShouldRejectV1PromptVersion()
    {
        var output = new CaptureTriageOutputV2(
            CaptureTriageOutputContract.SchemaVersionV2,
            CaptureTriageOutputContract.PromptVersionLlmV1,
            [new CaptureTriageTaskV2("Follow up with QA", "action", null, null, 0.9m, "I will follow up with QA")]);

        var result = CaptureTriageOutputContract.Validate(output);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(CaptureTriageOutputContract.PromptVersionLlmV2);
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

    // ---- Due-date plausibility (#2193) ------------------------------------------------------
    // A prompt with no reference date let gpt-4o-mini answer "Monday 1 September", spoken in
    // August 2026, with 2023-09-01, and a format-only contract carried it onto a card.

    /// <summary>The capture day used by the plausibility tests: the day the defect was reported.</summary>
    private static readonly DateOnly ReferenceDate = new(2026, 8, 29);

    [Fact]
    public void ReviewDueDateHint_ShouldKeepHintAndRaiseNoNote_WhenDateIsPlausible()
    {
        var reviewed = CaptureTriageOutputContract.ReviewDueDateHint(
            "2026-09-05",
            1,
            ReferenceDate,
            out var note);

        reviewed.Should().Be("2026-09-05");
        note.Should().BeNull();
    }

    [Fact]
    public void ReviewDueDateHint_ShouldDropHallucinatedYearWithAnHonestNote()
    {
        var reviewed = CaptureTriageOutputContract.ReviewDueDateHint(
            "2023-09-01",
            3,
            ReferenceDate,
            out var note);

        reviewed.Should().BeNull();
        note.Should().NotBeNull();
        note.Should().Contain("Task 3");
        note.Should().Contain("2023-09-01");
        note.Should().Contain("2026-08-29");
    }

    [Fact]
    public void ReviewDueDateHint_ShouldDropDateFarBeyondTheWindow()
    {
        var reviewed = CaptureTriageOutputContract.ReviewDueDateHint(
            "2036-01-01",
            1,
            ReferenceDate,
            out var note);

        reviewed.Should().BeNull();
        note.Should().Contain("2036-01-01");
    }

    [Theory]
    [InlineData("next Friday")]
    [InlineData("2026-02-30")]
    [InlineData("2026-9-5")]
    [InlineData(" 2026-09-05 ")]
    public void ReviewDueDateHint_ShouldDropHintThatIsNotACalendarDate(string dueDateHint)
    {
        var reviewed = CaptureTriageOutputContract.ReviewDueDateHint(
            dueDateHint,
            2,
            ReferenceDate,
            out var note);

        reviewed.Should().BeNull();
        note.Should().Contain("not a YYYY-MM-DD calendar date");
    }

    [Fact]
    public void ReviewDueDateHint_ShouldBoundTheQuotedHint_WhenTheModelReturnsArbitraryText()
    {
        var reviewed = CaptureTriageOutputContract.ReviewDueDateHint(
            new string('x', 500) + "\n" + new string('y', 500),
            1,
            ReferenceDate,
            out var note);

        reviewed.Should().BeNull();
        note.Should().NotBeNull();
        note!.Length.Should().BeLessThan(200);
        note.Should().NotContain("\n");
    }

    [Fact]
    public void ReviewDueDateHint_ShouldPassThroughNull()
    {
        var reviewed = CaptureTriageOutputContract.ReviewDueDateHint(null, 1, ReferenceDate, out var note);

        reviewed.Should().BeNull();
        note.Should().BeNull();
    }

    [Fact]
    public void IsWithinDueDatePlausibilityWindow_ShouldIncludeItsOwnEdgesAndExcludeOneDayBeyond()
    {
        var earliest = ReferenceDate.AddYears(-CaptureTriageOutputContract.MaxDueDateYearsBeforeReference);
        var latest = ReferenceDate.AddYears(CaptureTriageOutputContract.MaxDueDateYearsAfterReference);

        CaptureTriageOutputContract.IsWithinDueDatePlausibilityWindow(earliest, ReferenceDate).Should().BeTrue();
        CaptureTriageOutputContract.IsWithinDueDatePlausibilityWindow(latest, ReferenceDate).Should().BeTrue();
        CaptureTriageOutputContract.IsWithinDueDatePlausibilityWindow(ReferenceDate, ReferenceDate).Should().BeTrue();
        CaptureTriageOutputContract
            .IsWithinDueDatePlausibilityWindow(earliest.AddDays(-1), ReferenceDate).Should().BeFalse();
        CaptureTriageOutputContract
            .IsWithinDueDatePlausibilityWindow(latest.AddDays(1), ReferenceDate).Should().BeFalse();
    }

    [Fact]
    public void ValidateV2_ShouldRejectImplausibleDueDate_WhenAReferenceDateIsSupplied()
    {
        var result = CaptureTriageOutputContract.Validate(
            BuildOutputWithDueDateHint("2023-09-01"),
            ReferenceDate);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("2026-08-29");
    }

    [Fact]
    public void ValidateV2_ShouldAcceptPlausibleDueDate_WhenAReferenceDateIsSupplied()
    {
        var result = CaptureTriageOutputContract.Validate(
            BuildOutputWithDueDateHint("2026-09-05"),
            ReferenceDate);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tasks[0].DueDateHint.Should().Be("2026-09-05");
    }

    [Fact]
    public void ValidateV2_ShouldStayFormatOnly_WhenNoReferenceDateIsSupplied()
    {
        // The reference date is optional so stored payloads and callers that hold no capture day
        // keep the contract they were written against; the live path drops an implausible hint
        // earlier, at parse time.
        var result = CaptureTriageOutputContract.Validate(BuildOutputWithDueDateHint("2023-09-01"));

        result.IsSuccess.Should().BeTrue();
    }

    private static CaptureTriageOutputV2 BuildOutputWithDueDateHint(string dueDateHint) =>
        new(CaptureTriageOutputContract.SchemaVersionV2,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            [
                new CaptureTriageTaskV2(
                    "Send the revised budget",
                    "action",
                    "Alice",
                    dueDateHint,
                    0.9m,
                    "Alice: I'll send the revised budget on Monday 1 September.")
            ]);

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
    public void LlmV2TriageSchemaFile_ShouldDeclareRequiredMetadataAndStrictness()
    {
        var schemaPath = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "src",
            "Taskdeck.Application",
            "Schemas",
            "capture-triage-output.llm-v2.schema.json");

        File.Exists(schemaPath).Should().BeTrue();
        var schema = File.ReadAllText(schemaPath);
        schema.Should().Contain("\"const\": \"llm-triage.v2\"");
        schema.Should().Contain("\"evidenceQuote\"");
        schema.Should().Contain("\"additionalProperties\": false");
        schema.Should().Contain("\"required\"");
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
