namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Reports forward progress while a transcript-triage provider completion is in flight.
/// Implementations must be best-effort: failure to publish progress must never affect a
/// capture's deterministic-fallback behavior.
/// </summary>
public interface ILlmCaptureTriageProgressReporter
{
    void ReportProgress();
}
