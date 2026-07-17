namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Thrown by <see cref="BoundedFilterProvider"/> when a single extraction's
/// cumulative decoded output would cross the configured decompression ceiling.
/// It signals a decompression-bomb artefact, not an internal fault, so the
/// extractor catches it and records a content-free <c>decoded-size-limit</c>
/// warning row rather than surfacing an error. Internal to Infrastructure: the
/// filter provider and the PdfPig extractor that catches it live in the same
/// layer, and PdfPig itself may wrap it, so the extractor also consults
/// <see cref="BoundedFilterProvider.LimitExceeded"/> as the authoritative signal.
/// </summary>
internal sealed class ExtractionDecodedSizeLimitException : Exception
{
    public ExtractionDecodedSizeLimitException(long ceilingBytes)
        : base($"Decoded output exceeded the extraction ceiling of {ceilingBytes} bytes.")
    {
        CeilingBytes = ceilingBytes;
    }

    public long CeilingBytes { get; }
}
