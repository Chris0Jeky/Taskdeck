using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Local, deterministic text extraction adapter. Multiple adapters may be
/// registered; the extraction service chooses the first MIME match.
/// </summary>
public interface IArtefactTextExtractor
{
    string ExtractorName { get; }
    string ExtractorVersion { get; }

    bool CanExtract(string mimeType);

    Task<ArtefactExtractionResult> ExtractAsync(
        Stream content,
        CancellationToken cancellationToken = default);
}
