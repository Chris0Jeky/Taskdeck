using System.Collections.Concurrent;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Api.Workers;

public sealed class WorkerHeartbeatRegistry : ILlmCaptureTriageProgressReporter
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _heartbeats = new();
    public DateTimeOffset StartupTime { get; } = DateTimeOffset.UtcNow;

    public void ReportHeartbeat(string workerName)
    {
        if (string.IsNullOrWhiteSpace(workerName))
        {
            return;
        }

        _heartbeats[workerName] = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset? GetLastHeartbeat(string workerName)
    {
        if (_heartbeats.TryGetValue(workerName, out var heartbeat))
        {
            return heartbeat;
        }

        return null;
    }

    public void ReportProgress()
        => ReportHeartbeat(nameof(TranscriptTriageWorker));
}
