namespace Taskdeck.Application.Tests.Services.Eval;

/// <summary>
/// Result of evaluating a single eval case against actual output.
/// </summary>
/// <param name="Passed">Whether the eval case passed.</param>
/// <param name="ActualOutput">The actual output produced by the system.</param>
/// <param name="Explanation">Human-readable explanation of why the case passed or failed.</param>
public sealed record EvalResult(
    bool Passed,
    string ActualOutput,
    string Explanation);
