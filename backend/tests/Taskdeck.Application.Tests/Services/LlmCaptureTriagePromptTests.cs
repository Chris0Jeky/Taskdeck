using System.Globalization;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmCaptureTriagePromptTests
{
    /// <summary>
    /// The capture day from the #2193 acceptance run: a transcript spoken on 2026-08-29 saying
    /// "Monday 1 September" came back as 2023-09-01 because the prompt carried no reference date.
    /// </summary>
    private static readonly DateOnly CaptureDate = new(2026, 8, 29);

    [Fact]
    public void PromptVersion_ShouldMatchContractConstant()
    {
        LlmCaptureTriagePrompt.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
    }

    [Fact]
    public void BuildSystemPrompt_ShouldCarryTheReferenceDate()
    {
        var prompt = LlmCaptureTriagePrompt.BuildSystemPrompt(CaptureDate);

        prompt.Should().Contain("2026-08-29");
        prompt.Should().NotContain(LlmCaptureTriagePrompt.ReferenceDatePlaceholder);
    }

    [Fact]
    public void SystemPrompt_ShouldRenderTodayAndNeverShipThePlaceholder()
    {
        var expectedDay = LlmCaptureTriagePrompt.CurrentReferenceDate
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain(expectedDay);
        LlmCaptureTriagePrompt.SystemPrompt.Should()
            .NotContain(LlmCaptureTriagePrompt.ReferenceDatePlaceholder);
    }

    [Fact]
    public void ReferenceDatePlaceholder_ShouldBeExactlyAsLongAsTheDateThatReplacesIt()
    {
        // The extraction leg reserves quota against ONE rendered prompt, so the placeholder has to
        // be the same length as a rendered date or that estimate drifts. Comparing two rendered
        // prompts cannot prove this - both substitute a same-length string whatever it is - so
        // assert the substitution itself against a real formatted date.
        var rendered = new DateOnly(2026, 8, 29).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        rendered.Length.Should().Be(LlmCaptureTriagePrompt.ReferenceDatePlaceholder.Length);
        rendered.Length.Should().Be(CaptureTriageOutputContract.DueDateHintLength);
    }

    [Fact]
    public void BuildSystemPrompt_ShouldInstructPartialDateResolutionAndPinThePlausibilityWindow()
    {
        var prompt = LlmCaptureTriagePrompt.BuildSystemPrompt(CaptureDate);

        prompt.Should().Contain("reference date");
        prompt.Should().Contain("on or after the reference date");
        prompt.Should().Contain("never guess or invent a year");
        // A weekday name that disagrees with the resolved date must not drag the year forward:
        // 2026-09-01 is a Tuesday, and the first Monday 1 September is 2031 - outside the window.
        prompt.Should().Contain("A weekday name is not part of the date");
        prompt.Should().NotContain("for example \"Monday 1 September\"");
        prompt.Should().Contain(
            $"more than {CaptureTriageOutputContract.MaxDueDateYearsBeforeReference} years before");
        prompt.Should().Contain(
            $"more than {CaptureTriageOutputContract.MaxDueDateYearsAfterReference} years after");
    }

    [Fact]
    public void SystemPrompt_ShouldPinStrictV2TasksShapeAndLengthLimits()
    {
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain("\"tasks\"");
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain("\"type\"");
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain("\"assigneeHint\"");
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain("\"dueDateHint\"");
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain("\"confidence\"");
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain("\"evidenceQuote\"");
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain(
            CaptureTriageOutputContract.MaxTaskTitleLength.ToString());
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain(
            CaptureTriageOutputContract.MaxTaskEvidenceLength.ToString());
    }

    [Fact]
    public void BuildSystemPrompt_ShouldAllowForwardResolutionToCrossIntoTheNextYear()
    {
        // Round-2 review catch: forward resolution from a December reference NECESSARILY lands in
        // the next calendar year, so a blanket "never assume a different year" made the two rules
        // unsatisfiable together - a compliant model could emit the already-past 1 January (which
        // the two-year window accepts) or give up and return null.
        var prompt = LlmCaptureTriagePrompt.BuildSystemPrompt(new DateOnly(2026, 12, 31));

        prompt.Should().Contain("2026-12-31");
        prompt.Should().Contain("may fall in the calendar year AFTER the reference date");
        prompt.Should().Contain("\"1 January\" resolves to 2027-01-01");
        prompt.Should().NotContain("never assume a different year");
    }

    [Fact]
    public void TryParseTasks_ShouldKeepANextYearResolvedDate_FromADecemberCaptureDay()
    {
        var newYearsEve = new DateOnly(2026, 12, 31);

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(
            BuildSingleTaskContent("2027-01-01"),
            newYearsEve,
            out var tasks,
            out var notes);

        parsed.Should().BeTrue();
        tasks[0].DueDateHint.Should().Be("2027-01-01");
        notes.Should().BeEmpty();
    }

    [Fact]
    public void TryParseTasks_ShouldParseStrictV2Tasks_ForPlainJsonObject()
    {
        // The two-argument overload resolves against the server's current day, so the hint here is
        // expressed relative to it rather than pinned to a date that ages out of the window.
        var dueDateHint = LlmCaptureTriagePrompt.CurrentReferenceDate
            .AddDays(9)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var content = $$"""
                      {
                        "tasks": [
                          {
                            "title": "Send the budget to finance",
                            "type": "action",
                            "assigneeHint": "Bob",
                            "dueDateHint": "{{dueDateHint}}",
                            "confidence": 0.95,
                            "evidenceQuote": "Bob: I will send the budget over by Friday"
                          },
                          {
                            "title": "Schedule the review meeting",
                            "type": "decision",
                            "assigneeHint": null,
                            "dueDateHint": null,
                            "confidence": 0.8,
                            "evidenceQuote": "let's get the review on the calendar"
                          }
                        ]
                      }
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().HaveCount(2);
        tasks[0].Title.Should().Be("Send the budget to finance");
        tasks[0].Type.Should().Be("action");
        tasks[0].AssigneeHint.Should().Be("Bob");
        tasks[0].DueDateHint.Should().Be(dueDateHint);
        tasks[0].Confidence.Should().Be(0.95m);
        tasks[0].EvidenceQuote.Should().Be("Bob: I will send the budget over by Friday");
        tasks[1].AssigneeHint.Should().BeNull();
        tasks[1].DueDateHint.Should().BeNull();
    }

    [Fact]
    public void TryParseTasks_ShouldParseStrictV2Tasks_WhenJsonIsFencedWithSurroundingProse()
    {
        var content = """
                      Sure! Here are the action items I found:

                      ```json
                      {
                        "tasks": [
                          {
                            "title": "Follow up with QA",
                            "type": "action",
                            "assigneeHint": null,
                            "dueDateHint": null,
                            "confidence": 0.92,
                            "evidenceQuote": "I will follow up with QA tomorrow"
                          }
                        ]
                      }
                      ```

                      Let me know if you need anything else.
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().ContainSingle();
        tasks[0].Title.Should().Be("Follow up with QA");
        tasks[0].EvidenceQuote.Should().Be("I will follow up with QA tomorrow");
    }

    [Fact]
    public void TryParseTasks_ShouldRejectUnknownMissingDuplicateAndWronglyTypedFields()
    {
        var invalidContents = new[]
        {
            """{"reasoning":"extra root field","tasks":[]}""",
            """{"tasks":[{"title":"Follow up","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"quote","extra":"no"}]}""",
            """{"tasks":[{"title":"Follow up","type":"action","assigneeHint":null,"dueDateHint":null,"evidenceQuote":"quote"}]}""",
            """{"tasks":[{"title":"Follow up","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":"high","evidenceQuote":"quote"}]}""",
            """{"tasks":[{"title":"Follow up","type":"action","assigneeHint":false,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"quote"}]}""",
            """{"tasks":["not an object"]}""",
            """{"tasks":[],"tasks":[]}"""
        };

        foreach (var content in invalidContents)
        {
            var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

            parsed.Should().BeFalse(content);
            tasks.Should().BeEmpty(content);
        }
    }

    [Fact]
    public void TryParseTasks_ShouldReturnTrueWithEmptyList_ForEmptyTasksArray()
    {
        var parsed = LlmCaptureTriagePrompt.TryParseTasks("""{"tasks":[]}""", out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no braces in this response at all")]
    [InlineData("{ this is not valid json }")]
    [InlineData("{\"answer\": 42}")]
    [InlineData("{\"tasks\": \"not an array\"}")]
    [InlineData("{\"tasks\": {\"title\": \"object, not array\"}}")]
    [InlineData("[{\"title\": \"t\", \"evidenceQuote\": \"e\"}]")]
    [InlineData("{\"tasks\":[{\"title\":\"t\",\"evidence\":\"e\"}]}")]
    public void TryParseTasks_ShouldReturnFalse_ForUnusableContent(string? content)
    {
        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeFalse();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public void TryParseTasks_ShouldPreserveBracesInsideEvidenceQuote_WhenObjectClosesTheContent()
    {
        // The parser slices from the first '{' to the LAST '}' in the content. Braces inside
        // string values are safe as long as the object's own closing brace is the final '}'.
        var content = """
                      {
                        "tasks": [
                          {
                            "title": "Fix the parser",
                            "type": "question",
                            "assigneeHint": null,
                            "dueDateHint": null,
                            "confidence": 0.7,
                            "evidenceQuote": "the config { \"mode\": \"strict\" } broke parsing"
                          }
                        ]
                      }
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().ContainSingle();
        tasks[0].EvidenceQuote.Should().Be("the config { \"mode\": \"strict\" } broke parsing");
    }

    [Fact]
    public void TryParseTasks_ShouldNullHallucinatedYearAndReportIt_AgainstTheCaptureDate()
    {
        // The exact #2193 repro: "Monday 1 September" spoken on 2026-08-29, answered with 2023.
        var content = BuildSingleTaskContent("2023-09-01");

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, CaptureDate, out var tasks, out var notes);

        parsed.Should().BeTrue();
        tasks.Should().ContainSingle();
        tasks[0].Title.Should().Be("Send the revised budget");
        tasks[0].DueDateHint.Should().BeNull();
        notes.Should().ContainSingle();
        notes[0].Should().Contain("2023-09-01");
        notes[0].Should().Contain("2026-08-29");
    }

    [Fact]
    public void TryParseTasks_ShouldKeepADateTheModelResolvedAgainstTheCaptureDate()
    {
        // The regression fixture from the issue: resolved forward from 2026-08-29, "Monday 1
        // September" is 2026-09-01 and must survive untouched.
        var content = BuildSingleTaskContent("2026-09-01");

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, CaptureDate, out var tasks, out var notes);

        parsed.Should().BeTrue();
        tasks[0].DueDateHint.Should().Be("2026-09-01");
        notes.Should().BeEmpty();
    }

    [Fact]
    public void TryParseTasks_ShouldKeepAFullyQualifiedDate_ThatTheShippedRunAlsoGotRight()
    {
        var parsed = LlmCaptureTriagePrompt.TryParseTasks(
            BuildSingleTaskContent("2026-09-05"),
            CaptureDate,
            out var tasks,
            out var notes);

        parsed.Should().BeTrue();
        tasks[0].DueDateHint.Should().Be("2026-09-05");
        notes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2036-01-01")]
    [InlineData("next Friday")]
    [InlineData("2026-02-30")]
    public void TryParseTasks_ShouldDropAnUnusableDueDateWithoutLosingTheTask(string dueDateHint)
    {
        var parsed = LlmCaptureTriagePrompt.TryParseTasks(
            BuildSingleTaskContent(dueDateHint),
            CaptureDate,
            out var tasks,
            out var notes);

        // Dropping the hint rather than rejecting the output keeps every other extracted item:
        // a due date is a hint on a proposal a human still has to approve.
        parsed.Should().BeTrue();
        tasks.Should().ContainSingle();
        tasks[0].DueDateHint.Should().BeNull();
        notes.Should().ContainSingle();
    }

    [Fact]
    public void TryParseTasks_ShouldReportOneNotePerDroppedTask_UsingOneBasedPositions()
    {
        var content = $$"""
                      {
                        "tasks": [
                          {"title":"Keep this one","type":"action","assigneeHint":null,"dueDateHint":"2026-09-05","confidence":0.9,"evidenceQuote":"on the fifth of September"},
                          {"title":"Drop the date","type":"action","assigneeHint":null,"dueDateHint":"2023-09-01","confidence":0.9,"evidenceQuote":"Monday 1 September"}
                        ]
                      }
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, CaptureDate, out var tasks, out var notes);

        parsed.Should().BeTrue();
        tasks.Should().HaveCount(2);
        tasks[0].DueDateHint.Should().Be("2026-09-05");
        tasks[1].DueDateHint.Should().BeNull();
        notes.Should().ContainSingle();
        notes[0].Should().StartWith("Task 2");
    }

    [Fact]
    public void TryParseTasks_ShouldApplyTheWindowOnTheTwoArgumentOverload_UsingToday()
    {
        // The live extraction leg calls the two-argument shape; it must not be a bypass.
        var parsed = LlmCaptureTriagePrompt.TryParseTasks(
            BuildSingleTaskContent("2003-01-01"),
            out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().ContainSingle();
        tasks[0].DueDateHint.Should().BeNull();
    }

    private static string BuildSingleTaskContent(string dueDateHint) =>
        $$"""
        {
          "tasks": [
            {
              "title": "Send the revised budget",
              "type": "action",
              "assigneeHint": "Alice",
              "dueDateHint": "{{dueDateHint}}",
              "confidence": 0.9,
              "evidenceQuote": "Alice: I'll send the revised budget by Monday 1 September."
            }
          ]
        }
        """;

    [Fact]
    public void TryParseTasks_ShouldReturnFalse_WhenTrailingProseContainsClosingBrace()
    {
        // Documents the known limit of the first-{/last-} slice: a '}' in prose AFTER the JSON
        // object drags the slice past the object's real end, so the parse fails closed.
        var content = """
                      {"tasks":[{"title":"Fix the parser","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"a verbatim quote"}]}
                      Note: watch out for the stray } character in the config file.
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeFalse();
        tasks.Should().BeEmpty();
    }
}
