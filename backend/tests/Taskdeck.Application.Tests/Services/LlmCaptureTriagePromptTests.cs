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
        LlmCaptureTriagePrompt.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV1);
    }

    [Fact]
    public void SystemPrompt_ShouldPinTasksShapeAndLengthLimits()
    {
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain("\"tasks\"");
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain(
            CaptureTriageOutputContract.MaxTaskTitleLength.ToString());
        LlmCaptureTriagePrompt.SystemPrompt.Should().Contain(
            CaptureTriageOutputContract.MaxTaskEvidenceLength.ToString());
    }

    [Fact]
    public void TryParseTasks_ShouldParseTasks_ForPlainJsonObject()
    {
        var content = """
                      {
                        "tasks": [
                          { "title": "Send the budget to finance", "evidence": "I'll send the budget over by Friday" },
                          { "title": "Schedule the review meeting", "evidence": "let's get the review on the calendar" }
                        ]
                      }
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().HaveCount(2);
        tasks[0].Title.Should().Be("Send the budget to finance");
        tasks[0].Evidence.Should().Be("I'll send the budget over by Friday");
        tasks[1].Title.Should().Be("Schedule the review meeting");
        tasks[1].Evidence.Should().Be("let's get the review on the calendar");
    }

    [Fact]
    public void TryParseTasks_ShouldParseTasks_WhenJsonIsFencedWithSurroundingProse()
    {
        var content = """
                      Sure! Here are the action items I found:

                      ```json
                      {
                        "tasks": [
                          { "title": "Follow up with QA", "evidence": "I will follow up with QA tomorrow" }
                        ]
                      }
                      ```

                      Let me know if you need anything else.
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().HaveCount(1);
        tasks[0].Title.Should().Be("Follow up with QA");
        tasks[0].Evidence.Should().Be("I will follow up with QA tomorrow");
    }

    [Fact]
    public void TryParseTasks_ShouldIgnoreExtraJsonFields_OnObjectAndEntries()
    {
        var content = """
                      {
                        "reasoning": "two commitments were made",
                        "tasks": [
                          {
                            "title": "Follow up with QA",
                            "evidence": "I will follow up with QA tomorrow",
                            "confidence": 0.92
                          }
                        ]
                      }
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().HaveCount(1);
        tasks[0].Title.Should().Be("Follow up with QA");
        tasks[0].Evidence.Should().Be("I will follow up with QA tomorrow");
    }

    [Fact]
    public void TryParseTasks_ShouldReturnTrueWithEmptyList_ForEmptyTasksArray()
    {
        var parsed = LlmCaptureTriagePrompt.TryParseTasks("""{"tasks":[]}""", out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public void TryParseTasks_ShouldPreserveMalformedEntriesWithBlankFields_ForCallerSanitization()
    {
        // Every array element yields an entry (malformed ones with blank fields) so a non-empty
        // tasks array can never masquerade as the deliberate "no action items" empty verdict -
        // the two have different downstream semantics (deterministic fallback vs honest failure).
        // Dropping blank entries is the extractor's sanitization job, not the parser's.
        var content = """
                      {
                        "tasks": [
                          { "title": "Valid task", "evidence": "a valid verbatim quote" },
                          { "evidence": "entry with no title" },
                          { "title": "   ", "evidence": "entry with blank title" },
                          { "title": "entry with no evidence" },
                          { "title": "entry with blank evidence", "evidence": "" },
                          "not even an object"
                        ]
                      }
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().HaveCount(6);
        tasks[0].Title.Should().Be("Valid task");
        tasks[0].Evidence.Should().Be("a valid verbatim quote");
        tasks[1].Title.Should().BeEmpty();
        tasks[2].Title.Should().Be("   ");
        tasks[3].Evidence.Should().BeEmpty();
        tasks[5].Title.Should().BeEmpty();
        tasks[5].Evidence.Should().BeEmpty();
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
    [InlineData("[{\"title\": \"t\", \"evidence\": \"e\"}]")]
    public void TryParseTasks_ShouldReturnFalse_ForUnusableContent(string? content)
    {
        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeFalse();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public void TryParseTasks_ShouldPreserveBracesInsideStrings_WhenObjectClosesTheContent()
    {
        // The parser slices from the first '{' to the LAST '}' in the content. Braces inside
        // string values are safe as long as the object's own closing brace is the final '}'.
        var content = """
                      {
                        "tasks": [
                          { "title": "Fix the parser", "evidence": "the config { \"mode\": \"strict\" } broke parsing" }
                        ]
                      }
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeTrue();
        tasks.Should().HaveCount(1);
        tasks[0].Evidence.Should().Be("the config { \"mode\": \"strict\" } broke parsing");
    }

    [Fact]
    public void TryParseTasks_ShouldReturnFalse_WhenTrailingProseContainsClosingBrace()
    {
        // Documents the known limit of the first-{/last-} slice: a '}' in prose AFTER the JSON
        // object drags the slice past the object's real end, so the parse fails closed.
        var content = """
                      {"tasks":[{"title":"Fix the parser","evidence":"a verbatim quote"}]}
                      Note: watch out for the stray } character in the config file.
                      """;

        var parsed = LlmCaptureTriagePrompt.TryParseTasks(content, out var tasks);

        parsed.Should().BeFalse();
        tasks.Should().BeEmpty();
    }
}
