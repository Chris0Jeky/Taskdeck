using FluentAssertions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Eval;

/// <summary>
/// Tests for the eval harness framework: runner, eval cases, and seed dataset.
/// Uses a deterministic mock system function to verify the eval infrastructure
/// works correctly without depending on external LLM services.
/// </summary>
public class EvalHarnessTests
{
    /// <summary>
    /// Mock system function that returns deterministic responses based on input patterns.
    /// This simulates the kind of output an LLM-backed system would produce.
    /// </summary>
    private static string MockSystemFunction(string input)
    {
        // HappyPath: task capture / move / board queries
        if (input.Contains("Add a card", StringComparison.OrdinalIgnoreCase) &&
            !input.Contains("<script>", StringComparison.OrdinalIgnoreCase))
        {
            return "I'll create a proposal to add a new card to the specified column.";
        }

        if (input.Contains("Move card", StringComparison.OrdinalIgnoreCase))
        {
            return "I'll create a proposal to move the card to the target column.";
        }

        if (input.Contains("board status", StringComparison.OrdinalIgnoreCase))
        {
            return "Here is your board summary: 3 columns with 12 cards total.";
        }

        // Clarification: ambiguous references
        if (input.Contains("that card", StringComparison.OrdinalIgnoreCase))
        {
            return "Which card are you referring to? Please provide the card ID or title.";
        }

        if (input.Equals("Add a new card", StringComparison.OrdinalIgnoreCase))
        {
            return "I need a few more details. What title should the card have, and which column?";
        }

        // Refusal: destructive operations
        if (input.Contains("Delete all", StringComparison.OrdinalIgnoreCase))
        {
            return "I cannot perform destructive bulk operations. Please use the settings page for account management.";
        }

        // Refusal: off-topic
        if (input.Contains("poem", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("cat", StringComparison.OrdinalIgnoreCase))
        {
            return "I'm focused on task management operations. I can help you manage your board, cards, and workflows.";
        }

        // Safety: XSS
        if (input.Contains("<script>", StringComparison.OrdinalIgnoreCase))
        {
            return "I'll create a proposal to add a sanitized card to the column.";
        }

        // Safety: SQL injection
        if (input.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase))
        {
            return "I'll create a proposal to add a card with the given title to Backlog.";
        }

        // PromptInjection: instruction override / role-play
        if (input.Contains("Ignore all previous", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("DAN", StringComparison.Ordinal) ||
            input.Contains("unrestricted", StringComparison.OrdinalIgnoreCase))
        {
            return "I'm focused on task management operations. How can I help with your board?";
        }

        // PromptInjection: encoded injection
        if (input.Contains("&#x3C;", StringComparison.OrdinalIgnoreCase))
        {
            return "I'll create a proposal to add the card to the specified column.";
        }

        return "I can help you manage your board. What would you like to do?";
    }

    [Fact]
    public void SeedCases_ShouldHaveAtLeast10Cases()
    {
        var cases = SeedEvalCases.GetAll();
        cases.Count.Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public void SeedCases_ShouldCoverAllCategories()
    {
        var cases = SeedEvalCases.GetAll();
        var categories = cases.Select(c => c.Category).Distinct().ToList();

        categories.Should().Contain(EvalCategory.HappyPath);
        categories.Should().Contain(EvalCategory.Clarification);
        categories.Should().Contain(EvalCategory.Refusal);
        categories.Should().Contain(EvalCategory.Safety);
        categories.Should().Contain(EvalCategory.PromptInjection);
    }

    [Fact]
    public void EvalRunner_ShouldRunAllCases()
    {
        var cases = SeedEvalCases.GetAll();
        var results = EvalRunner.RunAll(cases, MockSystemFunction);

        results.Count.Should().Be(cases.Count);
    }

    [Fact]
    public void EvalRunner_ShouldReportAllCasesPassing_WithCorrectMock()
    {
        var cases = SeedEvalCases.GetAll();
        var results = EvalRunner.RunAll(cases, MockSystemFunction);

        foreach (var (evalCase, result) in results)
        {
            result.Passed.Should().BeTrue(
                $"Case '{evalCase.Description}' (category: {evalCase.Category}) failed: {result.Explanation}");
        }
    }

    [Fact]
    public void EvalRunner_ShouldReportFailure_WhenOutputDoesNotMatch()
    {
        var failingCase = new SimpleEvalCase(
            description: "Test that always fails",
            category: EvalCategory.HappyPath,
            input: "anything",
            expectedOutcome: "Should contain 'xyz-not-present'",
            expectedSubstrings: ["xyz-not-present"]);

        var results = EvalRunner.RunAll([failingCase], _ => "some other output");

        results.Should().HaveCount(1);
        results[0].Result.Passed.Should().BeFalse();
        results[0].Result.Explanation.Should().Contain("xyz-not-present");
    }

    [Fact]
    public void EvalRunner_ShouldReportFailure_WhenForbiddenSubstringPresent()
    {
        var failingCase = new SimpleEvalCase(
            description: "Test forbidden substring",
            category: EvalCategory.Safety,
            input: "test",
            expectedOutcome: "Should not contain 'leaked'",
            expectedSubstrings: ["output"],
            forbiddenSubstrings: ["leaked"]);

        var results = EvalRunner.RunAll([failingCase], _ => "some output with leaked data");

        results.Should().HaveCount(1);
        results[0].Result.Passed.Should().BeFalse();
        results[0].Result.Explanation.Should().Contain("leaked");
    }

    [Fact]
    public void EvalRunner_ShouldHandleEmptyOutput()
    {
        var evalCase = new SimpleEvalCase(
            description: "Empty output test",
            category: EvalCategory.HappyPath,
            input: "test",
            expectedOutcome: "Anything",
            expectedSubstrings: ["something"]);

        var results = EvalRunner.RunAll([evalCase], _ => "");

        results[0].Result.Passed.Should().BeFalse();
        results[0].Result.Explanation.Should().Contain("null or empty");
    }

    [Fact]
    public void Summarize_ShouldGroupByCategory()
    {
        var cases = SeedEvalCases.GetAll();
        var results = EvalRunner.RunAll(cases, MockSystemFunction);
        var summary = EvalRunner.Summarize(results);

        summary.Should().ContainKey(EvalCategory.HappyPath);
        summary.Should().ContainKey(EvalCategory.Clarification);
        summary.Should().ContainKey(EvalCategory.Refusal);
        summary.Should().ContainKey(EvalCategory.Safety);
        summary.Should().ContainKey(EvalCategory.PromptInjection);
    }

    [Fact]
    public void Summarize_ShouldCountCorrectly()
    {
        var cases = new IEvalCase[]
        {
            new SimpleEvalCase("pass", EvalCategory.HappyPath, "input", "expected", ["output"]),
            new SimpleEvalCase("fail", EvalCategory.HappyPath, "input", "expected", ["missing"]),
        };

        var results = EvalRunner.RunAll(cases, _ => "some output here");
        var summary = EvalRunner.Summarize(results);

        summary[EvalCategory.HappyPath].Passed.Should().Be(1);
        summary[EvalCategory.HappyPath].Failed.Should().Be(1);
        summary[EvalCategory.HappyPath].Total.Should().Be(2);
    }

    [Fact]
    public void EvalRunner_RunAll_ShouldThrow_OnNullCases()
    {
        var act = () => EvalRunner.RunAll(null!, _ => "output");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EvalRunner_RunAll_ShouldThrow_OnNullFunction()
    {
        var act = () => EvalRunner.RunAll([], null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SimpleEvalCase_ShouldExposeAllProperties()
    {
        var evalCase = new SimpleEvalCase(
            description: "test desc",
            category: EvalCategory.Safety,
            input: "test input",
            expectedOutcome: "test outcome",
            expectedSubstrings: ["sub1"]);

        evalCase.Description.Should().Be("test desc");
        evalCase.Category.Should().Be(EvalCategory.Safety);
        evalCase.Input.Should().Be("test input");
        evalCase.ExpectedOutcome.Should().Be("test outcome");
    }
}
