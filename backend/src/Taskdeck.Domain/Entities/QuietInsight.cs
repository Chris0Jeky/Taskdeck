using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class QuietInsight : Entity
{
    public Guid UserId { get; private set; }
    public Guid BoardId { get; private set; }
    public Guid? CardId { get; private set; }
    public Guid? MemoryId { get; private set; }
    public string Rule { get; private set; } = string.Empty;
    public string TargetKey { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Detail { get; private set; } = string.Empty;
    public string Evidence { get; private set; } = string.Empty;
    public string State { get; private set; } = "available";
    public DateTimeOffset CheckedAt { get; private set; }
    public DateTimeOffset? SnoozeUntil { get; private set; }
    public int Revision { get; private set; } = 1;
    private QuietInsight() { }
    public QuietInsight(Guid userId, Guid boardId, string rule, string targetKey, Guid? cardId, Guid? memoryId)
    { UserId = userId; BoardId = boardId; Rule = rule; TargetKey = targetKey; CardId = cardId; MemoryId = memoryId; }
    public void Refresh(string title, string detail, string evidence, DateTimeOffset now)
    {
        Title = title; Detail = detail; Evidence = evidence; CheckedAt = now;
        if (State == "resolved" || (State == "snoozed" && SnoozeUntil <= now)) State = "available";
        Revision++; Touch();
    }
    public void Resolve(DateTimeOffset now)
    { if (State is "available" or "snoozed") State = "resolved"; CheckedAt = now; Revision++; Touch(); }
    public void Act(string action, DateTimeOffset now)
    {
        State = action switch
        {
            "dismiss" => "dismissed", "mute" => "muted", "snooze" => "snoozed", "reopen" => "available",
            _ => throw new DomainException(ErrorCodes.ValidationError, "Choose dismiss, snooze, mute or reopen.")
        };
        SnoozeUntil = action == "snooze" ? now.AddDays(1) : null; Revision++; Touch();
    }
}
