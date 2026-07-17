using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class ArtefactStorageSettings
{
    public const long DefaultMaxBytesPerArtefact = 10L * 1024 * 1024;
    public const long DefaultMaxBytesPerUser = 200L * 1024 * 1024;
    public const double DefaultExtractionTimeoutSeconds = 30;

    [Range(typeof(long), "1", "2147483647")]
    public long MaxBytesPerArtefact { get; set; } = DefaultMaxBytesPerArtefact;

    [Range(typeof(long), "1", "9223372036854775807")]
    public long MaxBytesPerUser { get; set; } = DefaultMaxBytesPerUser;

    /// <summary>
    /// Wall-clock budget, in seconds, for a single artefact text extraction. The
    /// byte/page/character caps bound the input and persisted output but not the
    /// parser's in-memory work: a crafted PDF (deeply nested object streams,
    /// pathological content streams, decompression bombs) that stays under those
    /// caps can still spin PdfPig's synchronous parse arbitrarily long. This bounds
    /// that work. It is constrained to [1, 3600] seconds so an operator can raise it
    /// to effectively disable the budget without a zero/negative value that would
    /// abort every extraction.
    /// </summary>
    [Range(1, 3600)]
    public double ExtractionTimeoutSeconds { get; set; } = DefaultExtractionTimeoutSeconds;

    /// <summary>
    /// The extraction wall-clock budget as a <see cref="TimeSpan"/>. A value of
    /// zero or less (only reachable when the data-annotation range is bypassed, e.g.
    /// a hand-constructed settings object) is treated by the extraction service as
    /// "no budget" rather than an instant timeout.
    /// </summary>
    public TimeSpan ExtractionTimeout => TimeSpan.FromSeconds(ExtractionTimeoutSeconds);
}
