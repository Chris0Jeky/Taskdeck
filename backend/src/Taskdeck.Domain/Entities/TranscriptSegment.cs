using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A lightweight, line-indexed annotation within a <see cref="Transcript"/>.
/// Indices are zero-based and inclusive after the transcript has normalized its
/// line endings to LF.
/// </summary>
public sealed record TranscriptSegment
{
    public const int MaxSpeakerLength = 128;

    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string? Speaker { get; init; }
    public long? TimestampMilliseconds { get; init; }

    public TranscriptSegment(int startLine, int endLine, string? speaker = null, long? timestampMilliseconds = null)
    {
        if (startLine < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Transcript segment start line cannot be negative");
        if (endLine < startLine)
            throw new DomainException(ErrorCodes.ValidationError, "Transcript segment end line cannot precede its start line");
        if (speaker is not null && (string.IsNullOrWhiteSpace(speaker) || speaker.Length > MaxSpeakerLength ||
                                   speaker.Any(char.IsControl) || HasUnpairedSurrogate(speaker)))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Transcript segment speaker must be non-empty, control-free, and no longer than {MaxSpeakerLength} characters");
        }
        if (timestampMilliseconds is < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Transcript segment timestamp cannot be negative");

        StartLine = startLine;
        EndLine = endLine;
        Speaker = string.IsNullOrWhiteSpace(speaker) ? null : speaker;
        TimestampMilliseconds = timestampMilliseconds;
    }

    internal void ValidateWithinLineCount(int lineCount)
    {
        if (EndLine >= lineCount)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Transcript segment line range must be within the normalized transcript text");
        }
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    return true;
                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }
}
