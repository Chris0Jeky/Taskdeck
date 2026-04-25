namespace Taskdeck.Application.DTOs;

/// <summary>
/// Represents a single result from a nearest-neighbor vector search.
/// </summary>
public sealed record VectorSearchResult(
    string DocumentId,
    double Score,
    IReadOnlyDictionary<string, string>? Metadata = null);
