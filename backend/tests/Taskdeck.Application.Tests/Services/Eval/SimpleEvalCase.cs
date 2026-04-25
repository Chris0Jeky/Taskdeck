namespace Taskdeck.Application.Tests.Services.Eval;

/// <summary>
/// A simple eval case that checks whether the actual output contains
/// specific expected substrings or does NOT contain forbidden substrings.
/// Suitable for deterministic (non-LLM) evaluation targets.
/// </summary>
public sealed class SimpleEvalCase : IEvalCase
{
    public string Description { get; }
    public EvalCategory Category { get; }
    public string Input { get; }
    public string ExpectedOutcome { get; }

    private readonly IReadOnlyList<string> _expectedSubstrings;
    private readonly IReadOnlyList<string> _forbiddenSubstrings;

    public SimpleEvalCase(
        string description,
        EvalCategory category,
        string input,
        string expectedOutcome,
        IReadOnlyList<string> expectedSubstrings,
        IReadOnlyList<string>? forbiddenSubstrings = null)
    {
        Description = description;
        Category = category;
        Input = input;
        ExpectedOutcome = expectedOutcome;
        _expectedSubstrings = expectedSubstrings;
        _forbiddenSubstrings = forbiddenSubstrings ?? [];
    }

    public EvalResult Evaluate(string actualOutput)
    {
        if (string.IsNullOrEmpty(actualOutput))
        {
            return new EvalResult(false, actualOutput, "Actual output was null or empty.");
        }

        foreach (var expected in _expectedSubstrings)
        {
            if (!actualOutput.Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                return new EvalResult(
                    false,
                    actualOutput,
                    $"Expected substring '{expected}' not found in output.");
            }
        }

        foreach (var forbidden in _forbiddenSubstrings)
        {
            if (actualOutput.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return new EvalResult(
                    false,
                    actualOutput,
                    $"Forbidden substring '{forbidden}' found in output.");
            }
        }

        return new EvalResult(true, actualOutput, "All expected substrings found; no forbidden substrings present.");
    }
}
