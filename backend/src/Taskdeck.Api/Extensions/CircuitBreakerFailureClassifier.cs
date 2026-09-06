namespace Taskdeck.Api.Extensions;

/// <summary>
/// Builds the bounded failure reason that circuit-breaker <c>onBreak</c> callbacks
/// record on <see cref="Taskdeck.Application.Services.CircuitBreakerStateTracker"/>.
/// <para>
/// #2351 / R5: a Polly outcome carries the raw exception message, which can hold API
/// keys, bearer tokens, hostnames and local paths. Only the exception <em>type</em>
/// name, or the HTTP status code when there is no exception, is ever recorded, so a
/// future reader of the snapshot cannot leak transport detail. Every <c>onBreak</c>
/// site goes through this one method so the three of them cannot drift apart.
/// </para>
/// </summary>
internal static class CircuitBreakerFailureClassifier
{
    internal const string UnknownFailureReason = "Unknown failure";

    /// <summary>
    /// Returns the exception type name when <paramref name="exception"/> is present,
    /// otherwise <c>HTTP &lt;status&gt;</c>, otherwise a fixed placeholder. The
    /// exception message is never read.
    /// </summary>
    internal static string Classify(Exception? exception, int? statusCode)
    {
        if (exception is not null)
        {
            return exception.GetType().Name;
        }

        return statusCode is int status
            ? $"HTTP {status}"
            : UnknownFailureReason;
    }
}
