namespace Taskdeck.Application.Tests.Services.Eval;

/// <summary>
/// Defines a single eval test case for the LLM/automation eval harness.
/// Eval cases are self-contained: they carry their own input, expected
/// outcome, and validation logic. They do not depend on external services.
/// </summary>
public interface IEvalCase
{
    /// <summary>Human-readable description of what this case tests.</summary>
    string Description { get; }

    /// <summary>Category of this eval case.</summary>
    EvalCategory Category { get; }

    /// <summary>The input to feed to the system under test.</summary>
    string Input { get; }

    /// <summary>Description of the expected outcome.</summary>
    string ExpectedOutcome { get; }

    /// <summary>
    /// Evaluates the actual output against the expected outcome.
    /// Returns an <see cref="EvalResult"/> indicating pass or fail.
    /// </summary>
    EvalResult Evaluate(string actualOutput);
}
