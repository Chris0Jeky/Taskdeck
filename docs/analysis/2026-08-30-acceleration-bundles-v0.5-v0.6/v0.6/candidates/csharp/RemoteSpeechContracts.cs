
namespace Taskdeck.Acceleration.V06;

public sealed record RemoteSpeechRequest(
    string ContentHandle,
    string MediaType,
    string Sha256,
    long ByteSize,
    long? DurationMilliseconds,
    string? Language,
    bool WordTimestamps,
    bool Diarisation,
    int? MaximumSpeakers,
    string Region,
    DateTimeOffset Deadline,
    string IdempotencyKey);

public sealed record RemoteSpeechSegment(
    int CharStart,
    int CharEnd,
    long StartMilliseconds,
    long EndMilliseconds,
    string? SpeakerLabel,
    double? Confidence);

public sealed record RemoteSpeechUsage(
    decimal? BillableMinutes,
    decimal? EstimatedCost,
    string? Currency,
    bool IsAuthoritative);

public sealed record RemoteSpeechResult(
    string Status,
    string? Text,
    IReadOnlyList<RemoteSpeechSegment> Segments,
    IReadOnlyList<string> DiagnosticCodes,
    RemoteSpeechUsage Usage,
    string ProviderRequestId);

public interface IRemoteSpeechProviderAdapter
{
    Task<RemoteSpeechResult> TranscribeAsync(
        RemoteSpeechRequest request,
        CancellationToken cancellationToken);
}
