using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Explicit, size-bounded extraction orchestration. No HTTP endpoint or upload
/// hook calls this service in GEN-02; future workers may invoke it off the
/// request path without changing the persisted extraction contract.
/// </summary>
public sealed class ArtefactExtractionService : IArtefactExtractionService
{
    private readonly ISourceArtefactRepository _artefacts;
    private readonly IArtefactExtractionRepository _extractions;
    private readonly IReadOnlyList<IArtefactTextExtractor> _extractors;
    private readonly ILogger<ArtefactExtractionService>? _logger;

    public ArtefactExtractionService(
        ISourceArtefactRepository artefacts,
        IArtefactExtractionRepository extractions,
        IEnumerable<IArtefactTextExtractor> extractors,
        ILogger<ArtefactExtractionService>? logger = null)
    {
        _artefacts = artefacts;
        _extractions = extractions;
        _extractors = extractors.ToArray();
        _logger = logger;
    }

    public async Task<Result<ArtefactExtractionDto>> ExtractAsync(
        Guid userId,
        Guid sourceArtefactId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.ValidationError,
                "User ID cannot be empty");
        }
        if (sourceArtefactId == Guid.Empty)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.ValidationError,
                "Source artefact ID cannot be empty");
        }

        var artefact = await _artefacts.GetByIdForUserAsync(
            sourceArtefactId,
            userId,
            cancellationToken);
        if (artefact is null)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.NotFound,
                "Artefact not found");
        }

        var extractor = _extractors.FirstOrDefault(candidate => candidate.CanExtract(artefact.MimeType));
        if (extractor is null)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.ValidationError,
                $"No local text extractor supports MIME type '{artefact.MimeType}'");
        }
        if (!HasValidIdentity(extractor.ExtractorName, ArtefactExtraction.MaxExtractorNameLength) ||
            !HasValidIdentity(extractor.ExtractorVersion, ArtefactExtraction.MaxExtractorVersionLength) ||
            extractor.InputByteLimit is <= 0 or > int.MaxValue)
        {
            _logger?.LogError(
                "Artefact extractor registration has invalid identity for artefact {ArtefactId}",
                sourceArtefactId);
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.UnexpectedError,
                "The selected artefact extractor is misconfigured");
        }

        ArtefactExtractionResult? extractorResult = artefact.ByteSize > extractor.InputByteLimit
            ? WarningResult(extractor, ArtefactExtractionWarningCodes.InputTooLarge)
            : null;
        if (extractorResult is null)
        {
            await using var stream = new BoundedMemoryStream(extractor.InputByteLimit);
            var contentFound = false;
            try
            {
                contentFound = await _artefacts.CopyContentForUserAsync(
                    sourceArtefactId,
                    userId,
                    stream,
                    cancellationToken);
            }
            catch (ExtractionInputTooLargeException)
            {
                extractorResult = WarningResult(
                    extractor,
                    ArtefactExtractionWarningCodes.InputTooLarge);
            }

            if (extractorResult is null)
            {
                if (!contentFound)
                {
                    return Result.Failure<ArtefactExtractionDto>(
                        ErrorCodes.NotFound,
                        "Artefact not found");
                }

                stream.Position = 0;
                try
                {
                    extractorResult = await extractor.ExtractAsync(stream, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        "Local extraction failed for artefact {ArtefactId} with extractor {ExtractorName}; exception type {ExceptionType}",
                        sourceArtefactId,
                        extractor.ExtractorName,
                        ex.GetType().Name);
                    extractorResult = WarningResult(
                        extractor,
                        ArtefactExtractionWarningCodes.ExtractorError);
                }
            }
        }

        var sanitized = SanitizeResult(extractor, extractorResult);
        var extraction = new ArtefactExtraction(
            sourceArtefactId,
            extractor.ExtractorName,
            extractor.ExtractorVersion,
            sanitized.Warnings,
            sanitized.ExtractedText);

        var storeResult = await _extractions.TryAddForUserAsync(
            extraction,
            userId,
            cancellationToken);
        return storeResult switch
        {
            ArtefactExtractionStoreResult.Stored => Result.Success(Map(extraction)),
            ArtefactExtractionStoreResult.UserInactive => Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.Unauthorized,
                "The authenticated user is no longer active"),
            ArtefactExtractionStoreResult.SourceArtefactUnavailable => Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.NotFound,
                "Artefact not found"),
            _ => throw new InvalidOperationException($"Unknown extraction store result: {storeResult}")
        };
    }

    public async Task<Result<ArtefactExtractionDto>> GetLatestAsync(
        Guid userId,
        Guid sourceArtefactId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || sourceArtefactId == Guid.Empty)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.ValidationError,
                "User ID and source artefact ID are required");
        }

        var extraction = await _extractions.GetLatestForArtefactForUserAsync(
            sourceArtefactId,
            userId,
            cancellationToken);
        return extraction is null
            ? Result.Failure<ArtefactExtractionDto>(ErrorCodes.NotFound, "Artefact extraction not found")
            : Result.Success(Map(extraction));
    }

    private static ArtefactExtractionResult SanitizeResult(
        IArtefactTextExtractor extractor,
        ArtefactExtractionResult? result)
    {
        if (result is null)
        {
            return new ArtefactExtractionResult(
                string.Empty,
                [ArtefactExtractionWarningCodes.ExtractorContractError],
                extractor.ExtractorName,
                extractor.ExtractorVersion);
        }

        var contractError =
            !string.Equals(result.ExtractorName, extractor.ExtractorName, StringComparison.Ordinal) ||
            !string.Equals(result.ExtractorVersion, extractor.ExtractorVersion, StringComparison.Ordinal) ||
            result.ExtractedText is null ||
            result.Warnings is null;

        var warnings = new List<string>();
        if (result.Warnings is not null)
        {
            foreach (var warning in result.Warnings)
            {
                if (string.IsNullOrWhiteSpace(warning) ||
                    warning.Length > ArtefactExtraction.MaxWarningLength ||
                    warning.Any(char.IsControl) ||
                    ArtefactTextNormalization.HasUnpairedSurrogate(warning))
                {
                    contractError = true;
                    continue;
                }

                if (!warnings.Contains(warning, StringComparer.Ordinal))
                    warnings.Add(warning);
            }

            if (result.Warnings.Count > ArtefactExtraction.MaxWarningCount ||
                warnings.Count > ArtefactExtraction.MaxWarningCount)
            {
                contractError = true;
            }
        }

        var text = ArtefactTextNormalization.NormalizeLineEndings(result.ExtractedText ?? string.Empty);
        if (ArtefactTextNormalization.HasUnpairedSurrogate(text))
        {
            text = string.Empty;
            contractError = true;
        }
        if (text.Length > ArtefactExtraction.MaxExtractedTextLength)
        {
            text = ArtefactTextNormalization.TruncateWithoutSplittingSurrogatePair(
                text,
                ArtefactExtraction.MaxExtractedTextLength);
            AddPriorityWarning(warnings, ArtefactExtractionWarningCodes.CharacterLimit);
        }

        if (contractError)
            AddPriorityWarning(warnings, ArtefactExtractionWarningCodes.ExtractorContractError);

        warnings = warnings
            .Distinct(StringComparer.Ordinal)
            .Take(ArtefactExtraction.MaxWarningCount)
            .ToList();

        return new ArtefactExtractionResult(
            text,
            warnings,
            extractor.ExtractorName,
            extractor.ExtractorVersion);
    }

    private static bool HasValidIdentity(string value, int maxLength)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= maxLength &&
           !value.Any(char.IsControl) &&
           !ArtefactTextNormalization.HasUnpairedSurrogate(value);

    private static void AddPriorityWarning(List<string> warnings, string warning)
    {
        if (warnings.Contains(warning, StringComparer.Ordinal))
            return;
        while (warnings.Count >= ArtefactExtraction.MaxWarningCount)
            warnings.RemoveAt(warnings.Count - 1);
        warnings.Insert(0, warning);
    }

    private static ArtefactExtractionResult WarningResult(
        IArtefactTextExtractor extractor,
        string warning)
        => new(
            string.Empty,
            [warning],
            extractor.ExtractorName,
            extractor.ExtractorVersion);

    private static ArtefactExtractionDto Map(ArtefactExtraction extraction)
        => new(
            extraction.Id,
            extraction.SourceArtefactId,
            extraction.ExtractorName,
            extraction.ExtractorVersion,
            extraction.Warnings,
            extraction.ExtractedText,
            extraction.TextLength,
            extraction.CreatedAt);

    private sealed class BoundedMemoryStream : MemoryStream
    {
        private readonly long _maxBytes;

        public BoundedMemoryStream(long maxBytes)
            : base(capacity: (int)Math.Min(maxBytes, 64 * 1024))
        {
            _maxBytes = maxBytes;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWithinLimit(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWithinLimit(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureWithinLimit(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureWithinLimit(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            EnsureWithinLimit(1);
            base.WriteByte(value);
        }

        private void EnsureWithinLimit(int count)
        {
            if (Position > _maxBytes - count)
                throw new ExtractionInputTooLargeException();
        }
    }

    private sealed class ExtractionInputTooLargeException : IOException;
}
