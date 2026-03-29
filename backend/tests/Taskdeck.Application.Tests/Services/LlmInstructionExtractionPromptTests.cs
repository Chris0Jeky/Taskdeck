using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmInstructionExtractionPromptTests
{
    [Fact]
    public void TryParseStructuredResponse_ShouldParseValidActionableJson()
    {
        var json = """
            {
              "reply": "I'll create that card for you.",
              "actionable": true,
              "instructions": ["create card 'Onboarding task'"]
            }
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        reply.Should().Be("I'll create that card for you.");
        actionable.Should().BeTrue();
        instructions.Should().ContainSingle().Which.Should().Be("create card 'Onboarding task'");
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldParseMultipleInstructions()
    {
        var json = """
            {
              "reply": "I'll create those tasks.",
              "actionable": true,
              "instructions": [
                "create card 'Setup dev environment'",
                "create card 'Read onboarding docs'",
                "create card 'Meet the team'"
              ]
            }
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        actionable.Should().BeTrue();
        instructions.Should().HaveCount(3);
        instructions[0].Should().Be("create card 'Setup dev environment'");
        instructions[1].Should().Be("create card 'Read onboarding docs'");
        instructions[2].Should().Be("create card 'Meet the team'");
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldParseNonActionableJson()
    {
        var json = """
            {
              "reply": "Taskdeck is a project management tool.",
              "actionable": false,
              "instructions": []
            }
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        reply.Should().Be("Taskdeck is a project management tool.");
        actionable.Should().BeFalse();
        instructions.Should().BeEmpty();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldReturnFalse_ForPlainTextResponse()
    {
        var plainText = "Sure, I can help you create those tasks!";

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            plainText, out var reply, out var actionable, out var instructions);

        result.Should().BeFalse();
        reply.Should().BeEmpty();
        actionable.Should().BeFalse();
        instructions.Should().BeEmpty();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldReturnFalse_ForEmptyInput()
    {
        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            "", out var reply, out var actionable, out var instructions);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldReturnFalse_ForNullInput()
    {
        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            null!, out var reply, out var actionable, out var instructions);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldHandleMarkdownCodeFences()
    {
        var json = """
            ```json
            {
              "reply": "Creating your card.",
              "actionable": true,
              "instructions": ["create card 'Test'"]
            }
            ```
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        reply.Should().Be("Creating your card.");
        actionable.Should().BeTrue();
        instructions.Should().ContainSingle();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldReturnFalse_ForJsonWithoutReply()
    {
        var json = """
            {
              "actionable": true,
              "instructions": ["create card 'Test'"]
            }
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldSkipBlankInstructions()
    {
        var json = """
            {
              "reply": "Done.",
              "actionable": true,
              "instructions": ["create card 'Valid'", "", "  ", "create card 'Also valid'"]
            }
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        instructions.Should().HaveCount(2);
        instructions[0].Should().Be("create card 'Valid'");
        instructions[1].Should().Be("create card 'Also valid'");
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldHandleMissingInstructionsField()
    {
        var json = """
            {
              "reply": "Just a reply.",
              "actionable": false
            }
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        reply.Should().Be("Just a reply.");
        actionable.Should().BeFalse();
        instructions.Should().BeEmpty();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldHandleMalformedJson()
    {
        var json = """{ "reply": "oops", "actionable": tru """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out _, out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldHandleCodeFenceWithoutNewlineAfterLanguageTag()
    {
        // This is the edge case the old regex-based stripping would fail on
        var json = "```json{\"reply\":\"Done.\",\"actionable\":true,\"instructions\":[\"create card 'Test'\"]}```";

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        reply.Should().Be("Done.");
        actionable.Should().BeTrue();
        instructions.Should().ContainSingle().Which.Should().Be("create card 'Test'");
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldHandleCodeFenceWithoutLanguageSpecifier()
    {
        var json = """
            ```
            {
              "reply": "Here you go.",
              "actionable": false,
              "instructions": []
            }
            ```
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        reply.Should().Be("Here you go.");
        actionable.Should().BeFalse();
    }

    [Fact]
    public void TryParseStructuredResponse_ShouldHandleJsonWithSurroundingText()
    {
        var json = """
            Here is the result:
            {"reply": "Card created.", "actionable": true, "instructions": ["create card 'Demo'"]}
            Hope that helps!
            """;

        var result = LlmInstructionExtractionPrompt.TryParseStructuredResponse(
            json, out var reply, out var actionable, out var instructions);

        result.Should().BeTrue();
        reply.Should().Be("Card created.");
        actionable.Should().BeTrue();
        instructions.Should().ContainSingle().Which.Should().Be("create card 'Demo'");
    }

    [Fact]
    public void SystemPrompt_ShouldContainRequiredInstructionPatterns()
    {
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("create card");
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("move card");
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("archive card");
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("update card");
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("rename board");
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("move column");
    }

    [Fact]
    public void SystemPrompt_ShouldRequestJsonResponse()
    {
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("JSON");
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("reply");
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("actionable");
        LlmInstructionExtractionPrompt.SystemPrompt.Should().Contain("instructions");
    }
}
