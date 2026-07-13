using System.Text;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Local PDF text-layer extraction only. Input bytes, parser recursion, pages,
/// and output characters are all bounded; image OCR is deliberately absent.
/// </summary>
public sealed class PdfPigArtefactTextExtractor : IArtefactTextExtractor
{
    public const long MaxInputBytes = ArtefactStorageSettings.DefaultMaxBytesPerArtefact;
    public const int MaxPages = 100;
    public const int MaxExtractedCharacters = CaptureRequestContract.MaxTranscriptTextLength;
    public const int MaxParserStackDepth = 64;

    public string ExtractorName => "PdfPig";
    public string ExtractorVersion => "0.1.15";
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
        if (!content.CanSeek || content.Position != 0)
        {
            var bytes = await ReadBoundedAsync(content, MaxInputBytes, cancellationToken);
            if (bytes is null)
                return Warning(ArtefactExtractionWarningCodes.InputTooLarge);

            bufferedStream = new MemoryStream(bytes, writable: false);
            pdfStream = bufferedStream;
        }
        else if (content.Length > MaxInputBytes)
        {
            return Warning(ArtefactExtractionWarningCodes.InputTooLarge);
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
            if (document.NumberOfPages > MaxPages)
                warnings.Add(ArtefactExtractionWarningCodes.PageLimit);

            var text = new StringBuilder(capacity: Math.Min(MaxExtractedCharacters, 16 * 1024));
            var pageCount = Math.Min(document.NumberOfPages, MaxPages);
            for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                var pageText = ContentOrderTextExtractor.GetText(page, addDoubleNewline: false);
                if (string.IsNullOrWhiteSpace(pageText))
                    continue;

                if (text.Length > 0)
                {
                    if (text.Length == MaxExtractedCharacters)
                    {
                        warnings.Add(ArtefactExtractionWarningCodes.CharacterLimit);
                        break;
                    }

                    text.Append('\n');
                }

                var remaining = MaxExtractedCharacters - text.Length;
                if (pageText.Length <= remaining)
                {
                    text.Append(pageText);
                    continue;
                }

                var bounded = ArtefactTextNormalization.TruncateWithoutSplittingSurrogatePair(
                    pageText,
                    remaining);
                text.Append(bounded);
                warnings.Add(ArtefactExtractionWarningCodes.CharacterLimit);
                break;
            }

            if (string.IsNullOrWhiteSpace(text.ToString()))
            {
                warnings.Add(ArtefactExtractionWarningCodes.NoTextLayer);
                return new ArtefactExtractionResult(
                    string.Empty,
                    warnings.Distinct(StringComparer.Ordinal).ToArray(),
                    ExtractorName,
                    ExtractorVersion);
            }

            return new ArtefactExtractionResult(
                text.ToString(),
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
