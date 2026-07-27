namespace Taskdeck.Application.Services;

/// <summary>
/// Applies one configured provider deadline across request headers and bounded response-body reads.
/// The linked source preserves the caller token so providers can distinguish caller cancellation
/// from an internally expired deadline.
/// </summary>
internal static class LlmProviderDeadline
{
    internal static CancellationTokenSource CreateLinked(
        CancellationToken callerCancellationToken,
        int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds),
                "Provider timeout must be greater than zero.");
        }

        var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return deadline;
    }
}
