using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmCaptureTriagePromptTests
{
    [Fact]
    public void PromptVersion_ShouldMatchContractConstant()
    {
        LlmCaptureTriagePrompt.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
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
    public void TryParseTasks_ShouldParseStrictV2Tasks_ForPlainJsonObject()
    {
        var content = """
                      {
                        "tasks": [
                          {
                            "title": "Send the budget to finance",
                            "type": "action",
                            "assigneeHint": "Bob",
                            "dueDateHint": "2026-08-07",
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
        tasks[0].DueDateHint.Should().Be("2026-08-07");
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
