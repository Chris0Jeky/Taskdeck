using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Contracts;

internal sealed record BufferedArtefactUpload(
    byte[] Content,
    string FileName,
    string MimeType,
    Guid? BoardId,
    Guid? CreatedFromCaptureId);

/// <summary>
/// Parses the small artefact multipart contract without ASP.NET Core form binding.
/// File bytes are capped while they are read, before any complete upload can be
/// buffered by the API process or written to a temporary form file.
/// </summary>
internal static class ArtefactMultipartReader
{
    private const int MaxFieldBytes = 128;
    private const int MaxBoundaryLength = 128;
    private const int CopyBufferBytes = 64 * 1024;

    public static async Task<Result<BufferedArtefactUpload>> ReadAsync(
        HttpRequest request,
        ArtefactStorageSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ContentType))
            return Invalid("Artefact upload requires multipart/form-data content");

        MediaTypeHeaderValue mediaType;
        try
        {
            mediaType = MediaTypeHeaderValue.Parse(request.ContentType);
        }
        catch (FormatException)
        {
            return Invalid("Artefact upload has an invalid Content-Type header");
        }

        if (!mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return Invalid("Artefact upload requires multipart/form-data content");

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > MaxBoundaryLength)
            return Invalid("Artefact upload has an invalid multipart boundary");

        var reader = new MultipartReader(boundary, request.Body);
        byte[]? content = null;
        string? fileName = null;
        string? mimeType = null;
        Guid? boardId = null;
        Guid? createdFromCaptureId = null;
        var boardSeen = false;
        var captureSeen = false;

        try
        {
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                    !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
                {
                    return Invalid("Artefact upload contains an invalid multipart section");
                }

                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                var isFile = !StringSegment.IsNullOrEmpty(disposition.FileName) ||
                             !StringSegment.IsNullOrEmpty(disposition.FileNameStar);
                if (isFile)
                {
                    if (!string.Equals(name, "file", StringComparison.OrdinalIgnoreCase) || content is not null)
                        return Invalid("Artefact upload must contain exactly one file field");

                    var read = await ReadBoundedBytesAsync(
                        section.Body,
                        settings.MaxBytesPerArtefact,
                        cancellationToken);
                    if (!read.IsSuccess)
                        return Result.Failure<BufferedArtefactUpload>(read.ErrorCode, read.ErrorMessage);

                    content = read.Value;
                    fileName = HeaderUtilities.RemoveQuotes(
                        !StringSegment.IsNullOrEmpty(disposition.FileNameStar)
                            ? disposition.FileNameStar
                            : disposition.FileName).Value;
                    mimeType = section.ContentType ?? "application/octet-stream";
                    continue;
                }

                var field = await ReadBoundedFieldAsync(section.Body, cancellationToken);
                if (!field.IsSuccess)
                    return Result.Failure<BufferedArtefactUpload>(field.ErrorCode, field.ErrorMessage);

                if (string.Equals(name, "boardId", StringComparison.OrdinalIgnoreCase))
                {
                    if (boardSeen || !Guid.TryParse(field.Value, out var parsedBoardId))
                        return Invalid("Artefact upload has an invalid boardId field");
                    boardSeen = true;
                    boardId = parsedBoardId;
                }
                else if (string.Equals(name, "createdFromCaptureId", StringComparison.OrdinalIgnoreCase))
                {
                    if (captureSeen || !Guid.TryParse(field.Value, out var parsedCaptureId))
                        return Invalid("Artefact upload has an invalid createdFromCaptureId field");
                    captureSeen = true;
                    createdFromCaptureId = parsedCaptureId;
                }
                else
                {
                    return Invalid("Artefact upload contains an unsupported form field");
                }
            }
        }
        catch (IOException)
        {
            return Invalid("Artefact upload contains malformed multipart data");
        }

        if (content is null || fileName is null || mimeType is null)
            return Invalid("Artefact upload requires a file");

        return Result.Success(new BufferedArtefactUpload(
            content,
            fileName,
            mimeType,
            boardId,
            createdFromCaptureId));
    }

    internal static async Task<Result<byte[]>> ReadBoundedBytesAsync(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0 || maxBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        using var output = new MemoryStream(capacity: (int)Math.Min(maxBytes, 1024 * 1024));
        var buffer = new byte[CopyBufferBytes];
        long total = 0;
        while (true)
        {
            var nextReadLength = (int)Math.Min(buffer.Length, maxBytes - total + 1);
            var read = await source.ReadAsync(buffer.AsMemory(0, nextReadLength), cancellationToken);
            if (read == 0)
                return Result.Success(output.ToArray());

            total += read;
            if (total > maxBytes)
            {
                return Result.Failure<byte[]>(
                    ErrorCodes.PayloadTooLarge,
                    $"Artefact exceeds the configured {maxBytes}-byte size limit");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task<Result<string>> ReadBoundedFieldAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedBytesAsync(source, MaxFieldBytes, cancellationToken);
        if (!bytes.IsSuccess)
        {
            return Result.Failure<string>(
                ErrorCodes.ValidationError,
                $"Artefact upload form fields cannot exceed {MaxFieldBytes} bytes");
        }

        try
        {
            return Result.Success(new UTF8Encoding(false, true).GetString(bytes.Value).Trim());
        }
        catch (DecoderFallbackException)
        {
            return Result.Failure<string>(ErrorCodes.ValidationError, "Artefact upload form fields must be valid UTF-8");
        }
    }

    private static Result<BufferedArtefactUpload> Invalid(string message)
        => Result.Failure<BufferedArtefactUpload>(ErrorCodes.ValidationError, message);
}
