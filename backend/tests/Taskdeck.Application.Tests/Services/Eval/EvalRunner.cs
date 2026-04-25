namespace Taskdeck.Application.Tests.Services.Eval;

/// <summary>
/// Runs a set of eval cases against a system-under-test function.
/// The runner is intentionally simple: it calls the function for each
/// case and collects results. No external service dependencies.
///
/// Future iterations may integrate with promptfoo or other eval frameworks;
/// this version provides the foundational type system and runner loop.
/// </summary>
public static class EvalRunner
{
    /// <summary>
    /// Runs all eval cases through the provided system function and returns results.
    /// </summary>
    /// <param name="cases">The eval cases to run.</param>
    /// <param name="systemFunction">
    /// A function that takes an input string and returns the system's output.
    /// This could be a mock, a local classifier, or (in future) a real LLM call.
    /// </param>
    public static IReadOnlyList<(IEvalCase Case, EvalResult Result)> RunAll(
        IEnumerable<IEvalCase> cases,
        Func<string, string> systemFunction)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(systemFunction);

        var results = new List<(IEvalCase, EvalResult)>();

        foreach (var evalCase in cases)
        {
            var output = systemFunction(evalCase.Input);
            var result = evalCase.Evaluate(output);
            results.Add((evalCase, result));
        }

        return results;
    }

    /// <summary>
    /// Returns a summary of eval results by category.
    /// </summary>
    public static Dictionary<EvalCategory, (int Passed, int Failed, int Total)> Summarize(
        IReadOnlyList<(IEvalCase Case, EvalResult Result)> results)
    {
        var summary = new Dictionary<EvalCategory, (int Passed, int Failed, int Total)>();

        foreach (var (evalCase, result) in results)
        {
            if (!summary.TryGetValue(evalCase.Category, out var stats))
            {
                stats = (0, 0, 0);
            }

            summary[evalCase.Category] = result.Passed
                ? (stats.Passed + 1, stats.Failed, stats.Total + 1)
                : (stats.Passed, stats.Failed + 1, stats.Total + 1);
        }

        return summary;
    }
}
