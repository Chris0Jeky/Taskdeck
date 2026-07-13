using System.Text;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using UglyToad.PdfPig;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Local PDF text-layer extraction only. Input bytes, parser recursion, pages,
/// and output characters are all bounded; image OCR is deliberately absent.
/// </summary>
public sealed class PdfPigArtefactTextExtractor : IArtefactTextExtractor
{
    private static readonly string PdfPigVersion =
        typeof(PdfDocument).Assembly.GetName().Version?.ToString(3)
        ?? throw new InvalidOperationException("PdfPig assembly version is unavailable.");

    public const long MaxInputBytes = ArtefactStorageSettings.DefaultMaxBytesPerArtefact;
    public const int MaxPages = 100;
    public const int MaxExtractedCharacters = CaptureRequestContract.MaxTranscriptTextLength;
    public const int MaxParserStackDepth = 64;

    public string ExtractorName => "PdfPig";
    public string ExtractorVersion => PdfPigVersion;
    public long InputByteLimit => MaxInputBytes;

    public bool CanExtract(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return false;

        var normalized = mimeType.Split(';', 2)[0].Trim();
        return string.Equals(normalized, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ArtefactExtractionResult> ExtractAsync(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        Stream pdfStream = content;
        MemoryStream? bufferedStream = null;
        if (content.CanSeek)
        {
            // Seekable streams (the common case: a MemoryStream fed by the bounded
            // copy in ArtefactExtractionService) need no buffering. Enforce the input
            // cap up front, then rewind and read directly — copying up to 10 MiB into
            // a second MemoryStream would only add avoidable Large Object Heap pressure.
            if (content.Length > MaxInputBytes)
                return Warning(ArtefactExtractionWarningCodes.InputTooLarge);

            content.Position = 0;
        }
        else
        {
            // Non-seekable streams cannot be rewound and expose no reliable length, so
            // buffer them once through a bounded read that also enforces the input cap.
            var bytes = await ReadBoundedAsync(content, MaxInputBytes, cancellationToken);
            if (bytes is null)
                return Warning(ArtefactExtractionWarningCodes.InputTooLarge);

            bufferedStream = new MemoryStream(bytes, writable: false);
            pdfStream = bufferedStream;
        }

        try
        {
            var parsingOptions = new ParsingOptions
            {
                UseLenientParsing = false,
                UseActualText = true,
                MaxStackDepth = MaxParserStackDepth
            };
            using var document = PdfDocument.Open(pdfStream, parsingOptions);
            var warnings = new List<string>();
            var pagesSkipped = document.NumberOfPages > MaxPages;
            if (pagesSkipped)
                warnings.Add(ArtefactExtractionWarningCodes.PageLimit);

            var text = new StringBuilder(capacity: Math.Min(MaxExtractedCharacters, 16 * 1024));
            var pageCount = Math.Min(document.NumberOfPages, MaxPages);
            var characterLimitReached = false;
            for (var pageNumber = 1; pageNumber <= pageCount && !characterLimitReached; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);

                // Enforce the output budget while collecting, so a single text-heavy
                // page under the input cap cannot materialize an unbounded string or
                // burn CPU past the advertised limit before truncation. Words are
                // appended in reading order and collection stops the moment the budget
                // is exhausted; cancellation is observed between words rather than only
                // after a whole page is rendered.
                var firstWordOnPage = true;
                foreach (var word in page.GetWords())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var wordText = word.Text;
                    if (string.IsNullOrEmpty(wordText))
                        continue;

                    if (text.Length > 0)
                    {
                        if (text.Length >= MaxExtractedCharacters)
                        {
                            characterLimitReached = true;
                            break;
                        }

                        text.Append(firstWordOnPage ? '\n' : ' ');
                    }
                    firstWordOnPage = false;

                    var remaining = MaxExtractedCharacters - text.Length;
                    if (wordText.Length <= remaining)
                    {
                        text.Append(wordText);
                        continue;
                    }

                    text.Append(ArtefactTextNormalization.TruncateWithoutSplittingSurrogatePair(
                        wordText,
                        remaining));
                    characterLimitReached = true;
                    break;
                }
            }

            if (characterLimitReached)
                warnings.Add(ArtefactExtractionWarningCodes.CharacterLimit);

            var extractedText = text.ToString();
            if (characterLimitReached)
            {
                // At the exact cap boundary the last character appended can be a page
                // or word separator (the following word had zero remaining budget), so
                // strip trailing whitespace to never persist an extraction that ends in
                // a stray ' ' or '\n'. The CharacterLimit warning above is unaffected.
                extractedText = extractedText.TrimEnd();
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                // Only assert "no text layer" when every page was actually inspected.
                // If pages were skipped past MaxPages, the page-limit warning already
                // signals that unscanned pages may carry selectable text, so emitting
                // no-text-layer here would misdiagnose the document and push users
                // toward OCR for a file that does have a text layer further in.
                if (!pagesSkipped)
                    warnings.Add(ArtefactExtractionWarningCodes.NoTextLayer);

                return new ArtefactExtractionResult(
                    string.Empty,
                    warnings.Distinct(StringComparer.Ordinal).ToArray(),
                    ExtractorName,
                    ExtractorVersion);
            }

            return new ArtefactExtractionResult(
                extractedText,
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                ExtractorName,
                ExtractorVersion);
        }
        finally
        {
            if (bufferedStream is not null)
                await bufferedStream.DisposeAsync();
        }
    }

    private ArtefactExtractionResult Warning(string warning)
        => new(
            string.Empty,
            [warning],
            ExtractorName,
            ExtractorVersion);

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(capacity: (int)Math.Min(maxBytes, 1024 * 1024));
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return output.ToArray();

            total += read;
            if (total > maxBytes)
                return null;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
