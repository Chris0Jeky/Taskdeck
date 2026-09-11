using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Interfaces;

public interface ISourcePortabilityStore
{
    /// <summary>Hold a consistent read view across the storage sections; dispose before any write.</summary>
    Task<IAsyncDisposable> OpenReadSnapshotAsync(CancellationToken ct);
    Task<long> EstimateBufferedBytesAsync(Guid userId, CancellationToken ct);
    IAsyncEnumerable<SourceBlobObjectExportDto> ObjectsAsync(Guid userId, CancellationToken ct);
    IAsyncEnumerable<SourceBlobReferenceExportDto> ReferencesAsync(Guid userId, CancellationToken ct);
    IAsyncEnumerable<SourceBlobChunkExportDto> ChunksAsync(Guid userId, CancellationToken ct);
    IAsyncEnumerable<RepresentationDescriptor> RepresentationsAsync(Guid userId, CancellationToken ct);
    IAsyncEnumerable<ThinkingAudioExportDto> AudioAnswersAsync(Guid userId, CancellationToken ct);
    IAsyncEnumerable<AudioTranscriptionAttemptExportDto> AudioTranscriptionAttemptsAsync(Guid userId, CancellationToken ct);
    IAsyncEnumerable<AudioTranscriptionBudgetExportDto> AudioTranscriptionBudgetsAsync(Guid userId, CancellationToken ct);
}
