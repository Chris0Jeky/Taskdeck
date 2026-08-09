using System.Text.Json;
using System.ComponentModel.DataAnnotations.Schema;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// User-owned normalized transcript text. This is the sole durable home for
/// transcript text; proposals and source artefacts retain references only.
/// </summary>
public sealed class Transcript : Entity
{
    public const int MaxTextLength = 102_400;
    public const int MaxSegmentCount = 5_000;
    public const int MaxSegmentsJsonLength = 1_048_576;

    public Guid UserId { get; private set; }
    public Guid? BoardId { get; private set; }
    public CaptureSource CaptureSource { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public string SegmentsJson { get; private set; } = "[]";
    public Guid? CreatedFromCaptureId { get; private set; }
    public Guid? SourceArtefactId { get; private set; }

    private IReadOnlyList<TranscriptSegment>? _segments;

    [NotMapped]
    public IReadOnlyList<TranscriptSegment> Segments
        => _segments ??= JsonSerializer.Deserialize<TranscriptSegment[]>(SegmentsJson) ?? [];

    private Transcript() : base()
    {
    }

    public Transcript(
        Guid userId,
        CaptureSource captureSource,
        string text,
        IEnumerable<TranscriptSegment>? segments = null,
        Guid? boardId = null,
        Guid? createdFromCaptureId = null,
        Guid? sourceArtefactId = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");
        if (boardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");
        if (createdFromCaptureId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Capture ID cannot be empty");
        if (sourceArtefactId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Source artefact ID cannot be empty");
        if (!Enum.IsDefined(captureSource))
            throw new DomainException(ErrorCodes.ValidationError, "Capture source is invalid");
        if (text is null)
            throw new DomainException(ErrorCodes.ValidationError, "Transcript text cannot be null");

        var normalizedText = NormalizeLineEndings(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
            throw new DomainException(ErrorCodes.ValidationError, "Transcript text cannot be empty");
        if (normalizedText.Length > MaxTextLength)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Transcript text cannot exceed {MaxTextLength} characters");
        }
        if (HasUnpairedSurrogate(normalizedText))
            throw new DomainException(ErrorCodes.ValidationError, "Transcript text must contain valid UTF-16");

        var segmentList = segments?.ToArray() ?? [];
        if (segmentList.Length > MaxSegmentCount)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Transcript cannot contain more than {MaxSegmentCount} segments");
        }

        var lineCount = normalizedText.Count(character => character == '\n') + 1;
        foreach (var segment in segmentList)
        {
            if (segment is null)
                throw new DomainException(ErrorCodes.ValidationError, "Transcript segments cannot contain null values");
            segment.ValidateWithinLineCount(lineCount);
        }

        var segmentsJson = JsonSerializer.Serialize(segmentList);
        if (segmentsJson.Length > MaxSegmentsJsonLength)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Serialized transcript segments cannot exceed {MaxSegmentsJsonLength} characters");
        }

        UserId = userId;
        BoardId = boardId;
        CaptureSource = captureSource;
        Text = normalizedText;
        SegmentsJson = segmentsJson;
        _segments = segmentList;
        CreatedFromCaptureId = createdFromCaptureId;
        SourceArtefactId = sourceArtefactId;
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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
