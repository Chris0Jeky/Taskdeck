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

public sealed record ValidatedArtefactMetadata(ArtefactKind Kind, string MimeType, string FileName);

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

    // Path.GetInvalidFileNameChars() is minimal on Linux ({'\0','/'}), so the reserved
    // path/HTML metacharacters below are rejected cross-platform for consistent, hardened
    // filename validation regardless of the host OS.
    private static readonly char[] ReservedFileNameChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    public static async Task<Result<ValidatedArtefactContent>> ReadAndValidateAsync(
        Stream source,
        string fileName,
        string declaredMimeType,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var metadata = ValidateMetadata(fileName, declaredMimeType);
        if (!metadata.IsSuccess)
            return Result.Failure<ValidatedArtefactContent>(metadata.ErrorCode, metadata.ErrorMessage);

        var normalizedFileName = metadata.Value.FileName;
        var allowedType = AllowedTypes[metadata.Value.MimeType];

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

    public static Result<ValidatedArtefactMetadata> ValidateMetadata(string fileName, string declaredMimeType)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Result.Failure<ValidatedArtefactMetadata>(ErrorCodes.ValidationError, "Artefact file name is required");

        var normalizedFileName = fileName.Trim();
        if (normalizedFileName.Length > Domain.Entities.SourceArtefact.MaxFileNameLength ||
            Path.GetFileName(normalizedFileName) != normalizedFileName ||
            normalizedFileName.Contains('\\') ||
            normalizedFileName.Any(char.IsControl) ||
            normalizedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalizedFileName.IndexOfAny(ReservedFileNameChars) >= 0 ||
            // Reject Unicode format characters (e.g. U+202E right-to-left override) that can
            // spoof the displayed file name even though the stored bytes stay verified.
            normalizedFileName.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
        {
            return Result.Failure<ValidatedArtefactMetadata>(ErrorCodes.ValidationError, "Artefact file name is invalid");
        }

        var normalizedMimeType = declaredMimeType.Split(';', 2)[0].Trim();
        if (!AllowedTypes.TryGetValue(normalizedMimeType, out var allowedType))
        {
            return Result.Failure<ValidatedArtefactMetadata>(
                ErrorCodes.ValidationError,
                "Artefact content type is not allowed");
        }

        if (!allowedType.Extensions.Contains(Path.GetExtension(normalizedFileName)))
        {
            return Result.Failure<ValidatedArtefactMetadata>(
                ErrorCodes.ValidationError,
                "Artefact file extension does not match its content type");
        }

        return Result.Success(new ValidatedArtefactMetadata(
            allowedType.Kind, allowedType.MimeType, normalizedFileName));
    }

    public static Stream ValidateWhileReading(Stream source, ValidatedArtefactMetadata metadata)
        => new ValidatingReadStream(source, metadata);

    private sealed class ValidatingReadStream(Stream source, ValidatedArtefactMetadata metadata) : Stream
    {
        private readonly byte[] _signature = new byte[12];
        private readonly Decoder? _decoder = metadata.Kind == ArtefactKind.TextFile
            ? new UTF8Encoding(false, true).GetDecoder() : null;
        private int _signatureCount;
        private bool _completed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty) return 0;
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                if (!_completed)
                {
                    _completed = true;
                    ValidateBytes(ReadOnlySpan<byte>.Empty, flush: true);
                    if (metadata.Kind != ArtefactKind.TextFile &&
                        !AllowedTypes[metadata.MimeType].HasExpectedSignature(_signature.AsMemory(0, _signatureCount)))
                        throw new DomainException(ErrorCodes.ValidationError, "Artefact bytes do not match the declared content type");
                }
                return 0;
            }
            ObserveBytes(buffer[..read]);
            return read;
        }

        private void ObserveBytes(ReadOnlyMemory<byte> content)
        {
            var bytes = content.Span;
            var prefixLength = Math.Min(_signature.Length - _signatureCount, bytes.Length);
            bytes[..prefixLength].CopyTo(_signature.AsSpan(_signatureCount));
            _signatureCount += prefixLength;
            ValidateBytes(bytes, flush: false);
        }

        private void ValidateBytes(ReadOnlySpan<byte> bytes, bool flush)
        {
            if (_decoder is null) return;
            var chars = new char[bytes.Length + 1];
            try
            {
                var count = _decoder.GetChars(bytes, chars, flush);
                for (var index = 0; index < count; index++)
                    if (char.IsControl(chars[index]) && chars[index] is not '\r' and not '\n' and not '\t')
                        throw new DomainException(ErrorCodes.ValidationError, "Artefact bytes do not match the declared content type");
            }
            catch (DecoderFallbackException)
            {
                throw new DomainException(ErrorCodes.ValidationError, "Artefact bytes do not match the declared content type");
            }
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
