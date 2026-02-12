using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class CommandRun : Entity
{
    public string TemplateName { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public CommandRunStatus Status { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int? ExitCode { get; private set; }
    public bool Truncated { get; private set; }
    public string CorrelationId { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? OutputPreview { get; private set; }

    private readonly List<CommandRunLog> _logs = new();
    public IReadOnlyList<CommandRunLog> Logs => _logs.AsReadOnly();

    private CommandRun() { } // EF Core

    public CommandRun(
        string templateName,
        Guid requestedByUserId,
        string correlationId)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            throw new DomainException(ErrorCodes.ValidationError, "TemplateName cannot be empty");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "RequestedByUserId cannot be empty");
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new DomainException(ErrorCodes.ValidationError, "CorrelationId cannot be empty");

        TemplateName = templateName;
        RequestedByUserId = requestedByUserId;
        Status = CommandRunStatus.Queued;
        CorrelationId = correlationId;
        Truncated = false;
    }

    public void Start()
    {
        if (Status != CommandRunStatus.Queued)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot start command run in status {Status}");

        Status = CommandRunStatus.Running;
        StartedAt = DateTime.UtcNow;
        Touch();
    }

    public void Complete(int exitCode)
    {
        if (Status != CommandRunStatus.Running)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot complete command run in status {Status}");

        Status = CommandRunStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        ExitCode = exitCode;
        Touch();
    }

    public void Fail(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new DomainException(ErrorCodes.ValidationError, "ErrorMessage cannot be empty");
        if (Status != CommandRunStatus.Queued && Status != CommandRunStatus.Running)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot fail command run in status {Status}");

        Status = CommandRunStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void Timeout()
    {
        if (Status != CommandRunStatus.Running)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot timeout command run in status {Status}");

        Status = CommandRunStatus.TimedOut;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void Cancel()
    {
        if (Status != CommandRunStatus.Queued && Status != CommandRunStatus.Running)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot cancel command run in status {Status}");

        Status = CommandRunStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void SetTruncated()
    {
        Truncated = true;
        Touch();
    }

    public void SetOutputPreview(string outputPreview)
    {
        if (outputPreview != null && outputPreview.Length > 1000)
            throw new DomainException(ErrorCodes.ValidationError, "OutputPreview cannot exceed 1000 characters");

        OutputPreview = outputPreview;
        Touch();
    }

    public void AddLog(CommandRunLog log)
    {
        _logs.Add(log);
        Touch();
    }
}

public enum CommandRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    TimedOut,
    Cancelled
}
