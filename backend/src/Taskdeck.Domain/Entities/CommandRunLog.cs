using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class CommandRunLog : Entity
{
    public Guid CommandRunId { get; private set; }
    public DateTime Timestamp { get; private set; }
    public string Level { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? Metadata { get; private set; } // JSON

    // Navigation
    public CommandRun CommandRun { get; private set; } = null!;

    private CommandRunLog() { } // EF Core

    public CommandRunLog(
        Guid commandRunId,
        string level,
        string source,
        string message,
        string? metadata = null)
    {
        if (commandRunId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "CommandRunId cannot be empty");
        if (string.IsNullOrWhiteSpace(level))
            throw new DomainException(ErrorCodes.ValidationError, "Level cannot be empty");
        if (level != "Debug" && level != "Info" && level != "Warning" && level != "Error")
            throw new DomainException(ErrorCodes.ValidationError, "Level must be 'Debug', 'Info', 'Warning', or 'Error'");
        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException(ErrorCodes.ValidationError, "Source cannot be empty");
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException(ErrorCodes.ValidationError, "Message cannot be empty");

        CommandRunId = commandRunId;
        Timestamp = DateTime.UtcNow;
        Level = level;
        Source = source;
        Message = message;
        Metadata = metadata;
    }
}
