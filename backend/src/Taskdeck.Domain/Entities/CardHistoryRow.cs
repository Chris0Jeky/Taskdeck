using Taskdeck.Domain.Enums;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Value object representing a single row in a card's history ledger,
/// used in the proposal review History section.
/// Immutable after creation.
/// </summary>
public sealed class CardHistoryRow : IEquatable<CardHistoryRow>
{
    /// <summary>
    /// Sequential serial number formatted as '#001', '#002', etc.
    /// </summary>
    public string Serial { get; }

    /// <summary>
    /// Human-readable event description (e.g., "Card moved to In Progress").
    /// </summary>
    public string Event { get; }

    /// <summary>
    /// Pre-formatted relative time string (e.g., "11:42", "yest 16:04", "Mon 11:00", "Apr 15").
    /// </summary>
    public string Age { get; }

    /// <summary>
    /// Status classification: Pending (this proposal), Applied (previously applied), Past (other history).
    /// </summary>
    public CardHistoryStatus Status { get; }

    public CardHistoryRow(string serial, string @event, string age, CardHistoryStatus status)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("Serial cannot be empty.", nameof(serial));
        if (string.IsNullOrWhiteSpace(@event))
            throw new ArgumentException("Event cannot be empty.", nameof(@event));
        if (string.IsNullOrWhiteSpace(age))
            throw new ArgumentException("Age cannot be empty.", nameof(age));
        if (!Enum.IsDefined(status))
            throw new ArgumentException("Invalid CardHistoryStatus value.", nameof(status));

        Serial = serial;
        Event = @event;
        Age = age;
        Status = status;
    }

    public bool Equals(CardHistoryRow? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Serial == other.Serial
            && Event == other.Event
            && Age == other.Age
            && Status == other.Status;
    }

    public override bool Equals(object? obj) => Equals(obj as CardHistoryRow);

    public override int GetHashCode() => HashCode.Combine(Serial, Event, Age, Status);

    public override string ToString() => $"{Serial} {Event} ({Age}) [{Status}]";
}
