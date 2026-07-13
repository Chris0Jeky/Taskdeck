using System.Text;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services;

/// <summary>
/// Bounded UTF-8 passthrough for plain-text and Markdown artefacts. It mirrors
/// the existing FileContentValidator import limits instead of creating a second
/// text-safety policy.
/// </summary>
public sealed class PlainTextArtefactTextExtractor : IArtefactTextExtractor
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public const int MaxInputBytes = FileContentValidator.DefaultMaxTextContentBytes;
    public const int MaxInputCharacters = FileContentValidator.MaxMarkdownContentChars;

    public string ExtractorName => "PlainText";
    public string ExtractorVersion => "1.0";

    public bool CanExtract(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return false;

        var normalized = mimeType.Split(';', 2)[0].Trim();
        return string.Equals(normalized, "text/plain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "text/markdown", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ArtefactExtractionResult> ExtractAsync(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var bytes = await ReadBoundedAsync(content, MaxInputBytes, cancellationToken);
        if (bytes is null)
            return Warning(ArtefactExtractionWarningCodes.InputTooLarge);

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Warning(ArtefactExtractionWarningCodes.InvalidUtf8);
        }

        if (text.Length > MaxInputCharacters)
            return Warning(ArtefactExtractionWarningCodes.InputTooLarge);

        var validation = FileContentValidator.ValidateTextContent(
            text,
            "Artefact text",
            maxBytes: MaxInputBytes,
            maxChars: MaxInputCharacters);
        if (!validation.IsSuccess)
            return Warning(ArtefactExtractionWarningCodes.InvalidText);

        return new ArtefactExtractionResult(
            text,
            [],
            ExtractorName,
            ExtractorVersion);
    }

    private ArtefactExtractionResult Warning(string warning)
        => new(
            string.Empty,
            [warning],
            ExtractorName,
            ExtractorVersion);

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.CanSeek && content.Length - content.Position > maxBytes)
            return null;

        using var output = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[64 * 1024];
        var total = 0;
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
