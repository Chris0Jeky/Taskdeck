using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>Admission counts survive original/capture erasure; only account erasure removes this row.</summary>
public sealed class AudioTranscriptionBudget
{
    public Guid UserId { get; private set; }
    public int UtcDay { get; private set; }
    public int Attempts { get; private set; }
    public long InputBytes { get; private set; }
    public long Revision { get; private set; }
    private AudioTranscriptionBudget() { }
    public AudioTranscriptionBudget(Guid userId)
    {
        if (userId == Guid.Empty) throw new DomainException(ErrorCodes.ValidationError, "A transcription budget owner is required.");
        UserId = userId;
    }

    public bool Reserve(long inputBytes, int maximumAttempts, long maximumBytes, DateTimeOffset now)
    {
        if (inputBytes <= 0 || maximumAttempts <= 0 || maximumBytes <= 0 || now == default)
            throw new DomainException(ErrorCodes.ValidationError, "Positive transcription admission limits are required.");
        var day = now.UtcDateTime.Year * 10000 + now.UtcDateTime.Month * 100 + now.UtcDateTime.Day;
        if (day < UtcDay || inputBytes > maximumBytes) return false;
        var usedAttempts = day > UtcDay ? 0 : Attempts;
        var usedBytes = day > UtcDay ? 0 : InputBytes;
        if (usedAttempts >= maximumAttempts || inputBytes > maximumBytes - usedBytes) return false;
        UtcDay = day; Attempts = usedAttempts + 1; InputBytes = usedBytes + inputBytes; Revision++;
        return true;
    }
}
