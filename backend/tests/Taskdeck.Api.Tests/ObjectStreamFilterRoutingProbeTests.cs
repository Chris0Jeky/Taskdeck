using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using Xunit;
using Xunit.Abstractions;

namespace Taskdeck.Api.Tests;

/// <summary>
/// STEP 0 stop-gate evidence for issue #1379 / MEDIUM-4. Question: when PdfPig decodes
/// an object stream (<c>/Type /ObjStm</c>) and a cross-reference stream
/// (<c>/Type /XRef</c>) during <see cref="PdfDocument.Open(System.IO.Stream,ParsingOptions)"/>,
/// does the caller-injected <see cref="ParsingOptions.FilterProvider"/> get invoked?
///
/// VERDICT (proven below, empirically here and confirmed against PdfPig v0.1.15 source):
///   - Object streams  -> route through the injected provider.       COVERED.
///   - Cross-ref streams-> hard-coded DefaultFilterProvider.Instance. BYPASSED (unbounded).
///
/// PdfPig 0.1.15 <c>Parser/FileStructure/XrefStreamParser.TryReadStreamAtOffset</c> decodes
/// the xref stream with <c>stream.Decode(DefaultFilterProvider.Instance)</c> during the first
/// pass — before the wrapped provider exists — so <c>ParsingOptions.FilterProvider</c>
/// structurally cannot reach it. PdfPig's own per-stage size guard is skipped for the final
/// (or only) filter, so a single-filter FlateDecode xref stream is not bounded there either.
///
/// Consequence: the <see cref="BoundedFilterProvider"/> ceiling cannot contain a decompression
/// bomb carried in a cross-reference stream. The provider-injection containment boundary is
/// insufficient on its own; a different boundary (raw-bytes pre-scan, process/OS memory cap,
/// or an upstream PdfPig change threading the provider through xref decoding) is required.
/// These tests are committed as the parked-branch evidence; they must stay green (they pin the
/// current reality), and <see cref="XrefStreamDecode_ShouldRouteThroughInjectedProvider_WHEN_CONTAINMENT_FIXED"/>
/// is the quarantined target state to un-skip once the boundary is fixed.
/// </summary>
public sealed class ObjectStreamFilterRoutingProbeTests
{
    private const int ModestBombInflatedBytes = 16 * 1024 * 1024; // well above the 64 KiB ceiling, low OOM risk
    private const long TightCeilingBytes = 64 * 1024;

    private readonly ITestOutputHelper _output;

    public ObjectStreamFilterRoutingProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void HandCraftedObjectStreamPdf_IsValidAndOpensWithStockPdfPig()
    {
        // Guards the probe fixture: a malformed PDF would make every routing claim below
        // meaningless. Prove the hand-crafted ObjStm + xref-stream PDF really parses.
        var pdf = ObjectStreamPdf.Build();

        using var stream = new MemoryStream(pdf);
        using var document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = false });

        document.NumberOfPages.Should().Be(1);
    }

    [Fact]
    public void ObjectStreamDecode_RoutesThroughInjectedProvider()
    {
        var decodedTypes = RecordDecodedStreamTypes(ObjectStreamPdf.Build());

        decodedTypes.Should().Contain(
            "ObjStm",
            "object-stream decoding routes through the injected FilterProvider (PdfTokenScanner.ParseObjectStream)");
    }

    [Fact]
    public void XrefStreamDecode_BypassesInjectedProvider_KnownContainmentGap()
    {
        // THE STOP-GATE FACT. The injected provider never sees the /XRef stream decode:
        // PdfPig decodes it with a hard-coded DefaultFilterProvider.Instance during the
        // first pass. This test pins that gap so any future change that (correctly) starts
        // routing xref decodes through the provider will fail here and force a re-read of
        // the containment design. It is NOT an assertion that the gap is acceptable.
        var decodedTypes = RecordDecodedStreamTypes(ObjectStreamPdf.Build());
        _output.WriteLine("Stream /Type values decoded through the injected provider: "
            + string.Join(", ", decodedTypes));

        decodedTypes.Should().NotContain(
            "XRef",
            "cross-reference-stream decoding bypasses the injected FilterProvider on PdfPig 0.1.15 "
            + "(XrefStreamParser hard-codes DefaultFilterProvider.Instance) — the bounded ceiling cannot guard it");
    }

    [Fact(Skip = "Quarantined target state (#1379 stop-gate): un-skip only when the containment "
                 + "boundary routes xref-stream decoding through the injected provider, or is replaced "
                 + "by a boundary that does not depend on ParsingOptions.FilterProvider for xref streams.")]
    public void XrefStreamDecode_ShouldRouteThroughInjectedProvider_WHEN_CONTAINMENT_FIXED()
    {
        var decodedTypes = RecordDecodedStreamTypes(ObjectStreamPdf.Build());
        decodedTypes.Should().Contain("XRef");
    }

    [Fact]
    public async Task Extractor_CatchesObjectStreamFlateBomb_CoveredPath()
    {
        // Positive control: the ObjStm path IS covered, so a (valid-header) FlateDecode bomb
        // carried in an object stream is caught and recorded as decoded-size-limit.
        var bomb = ObjectStreamPdf.Build(objStmBombInflatedBytes: ModestBombInflatedBytes);
        var extractor = new PdfPigArtefactTextExtractor(
            new ArtefactStorageSettings { ExtractionMaxDecodedBytes = TightCeilingBytes });
        await using var stream = new MemoryStream(bomb);

        var result = await extractor.ExtractAsync(stream);

        result.Warnings.Should().Contain(
            ArtefactExtractionWarningCodes.DecodedSizeLimit,
            "the object-stream decode routes through the bounded provider and trips the ceiling");
    }

    [Fact]
    public async Task Extractor_DoesNotCatchXrefStreamFlateBomb_BypassedPath()
    {
        // THE EXPLOIT, demonstrated at the extractor boundary. A FlateDecode bomb in the
        // cross-reference stream is decoded with PdfPig's hard-coded DefaultFilterProvider,
        // so the bounded ceiling never fires: the inflated payload fully materializes and the
        // outcome is anything BUT decoded-size-limit. (Kept to 16 MiB so the demonstration
        // does not OOM the test host; a real attacker uses gigabytes.)
        var bomb = ObjectStreamPdf.Build(xrefBombInflatedBytes: ModestBombInflatedBytes);
        var extractor = new PdfPigArtefactTextExtractor(
            new ArtefactStorageSettings { ExtractionMaxDecodedBytes = TightCeilingBytes });
        await using var stream = new MemoryStream(bomb);

        var result = await extractor.ExtractAsync(stream);

        result.Warnings.Should().NotContain(
            ArtefactExtractionWarningCodes.DecodedSizeLimit,
            "the xref-stream decode bypasses the bounded provider, so the ceiling cannot fire — "
            + "this is the unbounded containment gap the stop-gate exists to surface");
    }

    private static IReadOnlyList<string> RecordDecodedStreamTypes(byte[] pdf)
    {
        var recording = new RecordingFilterProvider(DefaultFilterProvider.Instance);
        using var stream = new MemoryStream(pdf);
        // Mirror the extractor's ParsingOptions exactly (PdfPigArtefactTextExtractor).
        using var document = PdfDocument.Open(stream, new ParsingOptions
        {
            UseLenientParsing = false,
            UseActualText = true,
            MaxStackDepth = 64,
            FilterProvider = recording
        });
        _ = document.NumberOfPages;
        return recording.DecodedStreamTypes;
    }

    private sealed class RecordingFilterProvider : IFilterProvider
    {
        private readonly IFilterProvider _inner;

        public RecordingFilterProvider(IFilterProvider inner) => _inner = inner;

        public List<string> DecodedStreamTypes { get; } = new();

        public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary) => Wrap(_inner.GetFilters(dictionary));

        public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names) => Wrap(_inner.GetNamedFilters(names));

        public IReadOnlyList<IFilter> GetAllFilters() => Wrap(_inner.GetAllFilters());

        private IReadOnlyList<IFilter> Wrap(IReadOnlyList<IFilter> filters)
        {
            var wrapped = new IFilter[filters.Count];
            for (var i = 0; i < filters.Count; i++)
                wrapped[i] = new RecordingFilter(filters[i], this);
            return wrapped;
        }

        private void Record(DictionaryToken streamDictionary)
        {
            if (streamDictionary is not null &&
                streamDictionary.TryGet(NameToken.Type, out NameToken typeToken))
            {
                DecodedStreamTypes.Add(typeToken.Data);
            }
            else
            {
                DecodedStreamTypes.Add("(untyped)");
            }
        }

        private sealed class RecordingFilter : IFilter
        {
            private readonly IFilter _inner;
            private readonly RecordingFilterProvider _owner;

            public RecordingFilter(IFilter inner, RecordingFilterProvider owner)
            {
                _inner = inner;
                _owner = owner;
            }

            public bool IsSupported => _inner.IsSupported;

            public Memory<byte> Decode(
                Memory<byte> input,
                DictionaryToken streamDictionary,
                IFilterProvider filterProvider,
                int filterIndex)
            {
                _owner.Record(streamDictionary);
                return _inner.Decode(input, streamDictionary, filterProvider, filterIndex);
            }
        }
    }
}
