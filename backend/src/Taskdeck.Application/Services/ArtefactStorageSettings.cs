using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class ArtefactStorageSettings
{
    public const long DefaultMaxBytesPerArtefact = 10L * 1024 * 1024;
    public const long DefaultMaxBytesPerUser = 200L * 1024 * 1024;
    public const double DefaultExtractionTimeoutSeconds = 30;
    public const int DefaultExtractionMaxConcurrency = 2;
    public const long DefaultExtractionMaxDecodedBytes = 128L * 1024 * 1024;
    public const long MinExtractionMaxDecodedBytes = 64L * 1024;

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
    // Double literals set RangeAttribute.OperandType to double; the int-operand
    // overload would coerce the value to Int32 (rounding) before comparing, so a
    // sub-1s value like 0.6 would round up to 1 and slip past the floor while the
    // service still ran the raw sub-second budget.
    [Range(1.0, 3600.0)]
    public double ExtractionTimeoutSeconds { get; set; } = DefaultExtractionTimeoutSeconds;

    /// <summary>
    /// The extraction wall-clock budget as a <see cref="TimeSpan"/>. A value of
    /// zero or less (only reachable when the data-annotation range is bypassed, e.g.
    /// a hand-constructed settings object) is treated by the extraction service as
    /// "no budget" rather than an instant timeout.
    /// </summary>
    public TimeSpan ExtractionTimeout => TimeSpan.FromSeconds(ExtractionTimeoutSeconds);

    /// <summary>
    /// Maximum number of artefact extractions whose parse worker may run at once.
    /// A crafted parser-bomb PDF that never observes cancellation keeps a
    /// thread-pool thread at full CPU until PdfPig's synchronous parse completes
    /// (only the request is bounded by <see cref="ExtractionTimeoutSeconds"/>, not
    /// the abandoned thread). This bounds how many such parses can accumulate: once
    /// the permits are exhausted, further extractions are rejected pre-parse with
    /// <c>TooManyRequests</c> and spawn no new thread, so box-wide CPU burn is capped
    /// at this many spinning threads. Worst-case in-memory work is bounded at this
    /// value times <see cref="ExtractionMaxDecodedBytes"/>.
    /// </summary>
    // Integer literals set RangeAttribute.OperandType to int, matching this int
    // property; a string/double-operand range would coerce and misvalidate.
    [Range(1, 64)]
    public int ExtractionMaxConcurrency { get; set; } = DefaultExtractionMaxConcurrency;

    /// <summary>
    /// Cumulative cap, in bytes, on the decoded output a single extraction may
    /// materialize across every compressed stream in the artefact. The input byte
    /// cap bounds the compressed size, but a decompression-bomb stream (a few KiB of
    /// FlateDecode that inflates to gigabytes) stays under it while exhausting memory
    /// during the decode. The bounded filter provider counts decoded bytes as it goes
    /// and aborts the parse the moment this cumulative ceiling is crossed, recording a
    /// <c>decoded-size-limit</c> warning row rather than an out-of-memory crash. The
    /// floor is 64 KiB so an operator cannot configure a ceiling below a single decode
    /// buffer; the default (128 MiB) comfortably clears any legitimate ≤10 MiB PDF.
    /// </summary>
    // A string/long-operand range keeps the bound exact for the full Int64 span
    // (an int-operand range would overflow), mirroring MaxBytesPerUser above.
    [Range(typeof(long), "65536", "9223372036854775807")]
    public long ExtractionMaxDecodedBytes { get; set; } = DefaultExtractionMaxDecodedBytes;
}
