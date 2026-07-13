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
            !HasValidIdentity(extractor.ExtractorVersion, ArtefactExtraction.MaxExtractorVersionLength))
        {
            _logger?.LogError(
                "Artefact extractor registration has invalid identity for artefact {ArtefactId}",
                sourceArtefactId);
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.UnexpectedError,
                "The selected artefact extractor is misconfigured");
        }

        var content = await _artefacts.GetContentForUserAsync(
            sourceArtefactId,
            userId,
            cancellationToken);
        if (content is null)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.NotFound,
                "Artefact not found");
        }

        ArtefactExtractionResult extractorResult;
        try
        {
            await using var stream = new MemoryStream(content, writable: false);
            extractorResult = await extractor.ExtractAsync(stream, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Local extraction failed for artefact {ArtefactId} with extractor {ExtractorName}",
                sourceArtefactId,
                extractor.ExtractorName);
            extractorResult = new ArtefactExtractionResult(
                string.Empty,
                [ArtefactExtractionWarningCodes.ExtractorError],
                extractor.ExtractorName,
                extractor.ExtractorVersion);
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
                    warning.Any(char.IsControl))
                {
                    contractError = true;
                    continue;
                }

                if (!warnings.Contains(warning, StringComparer.Ordinal))
                    warnings.Add(warning);
            }
        }

        var text = ArtefactTextNormalization.NormalizeLineEndings(result.ExtractedText ?? string.Empty);
        if (text.Length > ArtefactExtraction.MaxExtractedTextLength)
        {
            text = ArtefactTextNormalization.TruncateWithoutSplittingSurrogatePair(
                text,
                ArtefactExtraction.MaxExtractedTextLength);
            warnings.Add(ArtefactExtractionWarningCodes.CharacterLimit);
        }

        if (contractError)
            warnings.Add(ArtefactExtractionWarningCodes.ExtractorContractError);

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
           !value.Any(char.IsControl);

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
}
