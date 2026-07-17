using System.IO.Compression;
using System.Runtime.InteropServices;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Decorates PdfPig's <see cref="DefaultFilterProvider"/> to bound the total
/// decoded output a single extraction may materialize. Injected per parse via
/// <c>ParsingOptions.FilterProvider</c>. The input byte cap bounds the compressed
/// size, but a decompression-bomb stream (a few KiB of FlateDecode that inflates
/// to gigabytes) stays under it while exhausting memory during the real decode.
///
/// It never reimplements a filter: it counts, then delegates. Three complementary
/// checks share one cumulative budget across every stream in the document:
/// <list type="bullet">
///   <item>FlateDecode (the practical bomb vector): a cheap streaming counting
///   pre-pass inflates the raw bytes into a fixed discard buffer and aborts the
///   moment the running total would cross the remaining budget — before the real
///   decode allocates anything. Predictors / DecodeParms stay PdfPig's problem.</item>
///   <item>LZW (rare, no cheap .NET counter): conservative admission by worst-case
///   expansion ratio.</item>
///   <item>Every filter: a post-decode backstop adds the actual output length to the
///   cumulative counter, catching multi-stream accumulation and non-Flate growth.</item>
/// </list>
/// On breach it sets <see cref="LimitExceeded"/> and throws
/// <see cref="ExtractionDecodedSizeLimitException"/>. The extractor treats the flag
/// as authoritative because PdfPig may wrap or swallow the thrown exception.
/// </summary>
internal sealed class BoundedFilterProvider : IFilterProvider
{
    private const int DiscardBufferBytes = 64 * 1024;

    private readonly IFilterProvider _inner;
    private readonly long _ceilingBytes;

    // A single extraction parse is synchronous and single-threaded (PdfPig's
    // PdfDocument.Open and the per-page GetWords all run on one worker thread), so
    // this running total needs no synchronization.
    private long _decodedBytes;

    public BoundedFilterProvider(IFilterProvider inner, long ceilingBytes)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ceilingBytes = ceilingBytes;
    }

    /// <summary>
    /// True once any stream crossed the ceiling. Authoritative: the extractor checks
    /// this even when the thrown <see cref="ExtractionDecodedSizeLimitException"/> was
    /// wrapped or swallowed inside PdfPig.
    /// </summary>
    public bool LimitExceeded { get; private set; }

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary)
        => Wrap(_inner.GetFilters(dictionary));

    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names)
        => Wrap(_inner.GetNamedFilters(names));

    public IReadOnlyList<IFilter> GetAllFilters()
        => Wrap(_inner.GetAllFilters());

    private IReadOnlyList<IFilter> Wrap(IReadOnlyList<IFilter> filters)
    {
        var wrapped = new IFilter[filters.Count];
        for (var i = 0; i < filters.Count; i++)
            wrapped[i] = new BoundedFilter(filters[i], this);
        return wrapped;
    }

    private long Remaining => Math.Max(0L, _ceilingBytes - _decodedBytes);

    private void MarkExceeded()
    {
        LimitExceeded = true;
        throw new ExtractionDecodedSizeLimitException(_ceilingBytes);
    }

    private void GuardFlateBeforeDecode(Memory<byte> input)
    {
        var remaining = Remaining;
        if (remaining <= 0)
            MarkExceeded();

        if (!MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)input, out var segment) ||
            segment.Array is null)
        {
            // Cannot cheaply obtain the backing array to pre-scan; the post-decode
            // backstop still bounds whatever the real filter produces.
            return;
        }

        var discard = new byte[DiscardBufferBytes];
        long produced = 0;
        try
        {
            using var source = new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false);
            using var inflate = new ZLibStream(source, CompressionMode.Decompress);
            int read;
            while ((read = inflate.Read(discard, 0, discard.Length)) > 0)
            {
                produced += read;
                if (produced > remaining)
                    MarkExceeded();
            }
        }
        catch (ExtractionDecodedSizeLimitException)
        {
            throw;
        }
        catch (Exception)
        {
            // Corrupt or non-zlib Flate data: defer to the real filter (it may repair
            // or raise its own error); the post-decode backstop still applies.
        }
    }

    private void GuardLzwBeforeDecode(Memory<byte> input)
    {
        // No cheap streaming LZW counter in .NET. A PDF LZW stream cannot expand more
        // than ~1:256, so reject up front when even that worst case would cross the
        // budget. Legacy files under the bound decode normally (and are still checked
        // by the post-decode backstop).
        if ((long)input.Length * 256 > Remaining)
            MarkExceeded();
    }

    private void CountDecoded(long producedBytes)
    {
        _decodedBytes += producedBytes;
        if (_decodedBytes > _ceilingBytes)
            MarkExceeded();
    }

    private sealed class BoundedFilter : IFilter
    {
        private readonly IFilter _inner;
        private readonly BoundedFilterProvider _budget;

        public BoundedFilter(IFilter inner, BoundedFilterProvider budget)
        {
            _inner = inner;
            _budget = budget;
        }

        public bool IsSupported => _inner.IsSupported;

        public Memory<byte> Decode(
            Memory<byte> input,
            DictionaryToken streamDictionary,
            IFilterProvider filterProvider,
            int filterIndex)
        {
            // Identify the concrete filter by runtime type name rather than a compile
            // -time reference, so an internal filter type never breaks the decorator.
            var filterKind = _inner.GetType().Name;
            if (filterKind == "FlateFilter")
                _budget.GuardFlateBeforeDecode(input);
            else if (filterKind == "LzwFilter")
                _budget.GuardLzwBeforeDecode(input);

            // Delegate the real decode (predictors, DecodeParms, nested providers) to
            // the wrapped filter — we only ever count, never reimplement.
            var output = _inner.Decode(input, streamDictionary, filterProvider, filterIndex);

            _budget.CountDecoded(output.Length);
            return output;
        }
    }
}
