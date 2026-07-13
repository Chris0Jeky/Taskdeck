using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed record ValidatedArtefactContent(
    ArtefactKind Kind,
    string MimeType,
    string FileName,
    byte[] Bytes,
    string Sha256);

/// <summary>
/// Binary-aware validation lane for source artefacts. This deliberately does not
/// use the text-import FileContentValidator.
/// </summary>
public static class ArtefactContentValidator
{
    private sealed record AllowedType(
        ArtefactKind Kind,
        string MimeType,
        IReadOnlySet<string> Extensions,
        Func<ReadOnlyMemory<byte>, bool> HasExpectedSignature);

    private static readonly IReadOnlyDictionary<string, AllowedType> AllowedTypes =
        new Dictionary<string, AllowedType>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = new(
                ArtefactKind.Image,
                "image/png",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png" },
                bytes => bytes.Span.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })),
            ["image/jpeg"] = new(
                ArtefactKind.Image,
                "image/jpeg",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" },
                bytes => bytes.Length >= 3 && bytes.Span[0] == 0xFF && bytes.Span[1] == 0xD8 && bytes.Span[2] == 0xFF),
            ["image/webp"] = new(
                ArtefactKind.Image,
                "image/webp",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".webp" },
                bytes => bytes.Length >= 12 &&
                    bytes.Span[..4].SequenceEqual("RIFF"u8) &&
                    bytes.Span.Slice(8, 4).SequenceEqual("WEBP"u8)),
            ["application/pdf"] = new(
                ArtefactKind.Pdf,
                "application/pdf",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" },
                bytes => bytes.Span.StartsWith("%PDF-"u8)),
            ["text/plain"] = new(
                ArtefactKind.TextFile,
                "text/plain",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" },
                HasValidUtf8Text),
            ["text/markdown"] = new(
                ArtefactKind.TextFile,
                "text/markdown",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" },
                HasValidUtf8Text)
        };

    public static async Task<Result<ValidatedArtefactContent>> ReadAndValidateAsync(
        Stream source,
        string fileName,
        string declaredMimeType,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (string.IsNullOrWhiteSpace(fileName))
            return Result.Failure<ValidatedArtefactContent>(ErrorCodes.ValidationError, "Artefact file name is required");

        var normalizedFileName = fileName.Trim();
        if (normalizedFileName.Length > Domain.Entities.SourceArtefact.MaxFileNameLength ||
            Path.GetFileName(normalizedFileName) != normalizedFileName ||
            normalizedFileName.Contains('\\') ||
            normalizedFileName.Any(char.IsControl) ||
            normalizedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Result.Failure<ValidatedArtefactContent>(ErrorCodes.ValidationError, "Artefact file name is invalid");
        }

        var normalizedMimeType = declaredMimeType.Split(';', 2)[0].Trim();
        if (!AllowedTypes.TryGetValue(normalizedMimeType, out var allowedType))
        {
            return Result.Failure<ValidatedArtefactContent>(
                ErrorCodes.ValidationError,
                "Artefact content type is not allowed");
        }

        if (!allowedType.Extensions.Contains(Path.GetExtension(normalizedFileName)))
        {
            return Result.Failure<ValidatedArtefactContent>(
                ErrorCodes.ValidationError,
                "Artefact file extension does not match its content type");
        }

        using var output = new MemoryStream(capacity: (int)Math.Min(maxBytes, 1024 * 1024));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
            {
                return Result.Failure<ValidatedArtefactContent>(
                    ErrorCodes.PayloadTooLarge,
                    $"Artefact exceeds the configured {maxBytes}-byte size limit");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total == 0)
            return Result.Failure<ValidatedArtefactContent>(ErrorCodes.ValidationError, "Artefact content cannot be empty");

        var bytes = output.ToArray();
        if (!allowedType.HasExpectedSignature(bytes))
        {
            return Result.Failure<ValidatedArtefactContent>(
                ErrorCodes.ValidationError,
                "Artefact bytes do not match the declared content type");
        }

        return Result.Success(new ValidatedArtefactContent(
            allowedType.Kind,
            allowedType.MimeType,
            normalizedFileName,
            bytes,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
    }

    private static bool HasValidUtf8Text(ReadOnlyMemory<byte> bytes)
    {
        var remaining = bytes.Span;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
                return false;
            if (Rune.IsControl(rune) && rune.Value is not '\r' and not '\n' and not '\t')
                return false;

            remaining = remaining[consumed..];
        }

        return true;
    }
}
