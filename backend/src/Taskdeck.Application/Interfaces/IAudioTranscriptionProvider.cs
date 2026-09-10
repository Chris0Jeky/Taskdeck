using Taskdeck.Application.Services;

namespace Taskdeck.Application.Interfaces;

public interface IAudioTranscriptionProvider
{
    SpeechTranscriptionConfiguration Configuration { get; }
    Task<AudioTranscriptionProviderResult> TranscribeAsync(byte[] original, string mediaType, CancellationToken ct);
}

public sealed record AudioTranscriptionProviderResult(string? Text, string? FailureCode);
