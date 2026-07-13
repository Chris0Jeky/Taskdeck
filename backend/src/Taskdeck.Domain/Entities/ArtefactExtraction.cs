using System.Text.Json;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Immutable, reviewable text extracted from a source artefact. Re-extraction
/// appends a new record; consumers select the latest record instead of mutating
/// extraction history.
/// </summary>
public sealed class ArtefactExtraction : Entity
{
    public const int MaxExtractorNameLength = 100;
    public const int MaxExtractorVersionLength = 50;
    public const int MaxWarningLength = 128;
    public const int MaxWarningCount = 16;
    public const int MaxWarningsJsonLength = 4096;
    public const int MaxExtractedTextLength = 102_400;

    public Guid SourceArtefactId { get; private set; }
    public string ExtractorName { get; private set; } = string.Empty;
    public string ExtractorVersion { get; private set; } = string.Empty;
    public string WarningsJson { get; private set; } = "[]";
    public string ExtractedText { get; private set; } = string.Empty;
    public int TextLength { get; private set; }

    public IReadOnlyList<string> Warnings
        => JsonSerializer.Deserialize<string[]>(WarningsJson) ?? [];

    private ArtefactExtraction() : base()
    {
    }

    public ArtefactExtraction(
        Guid sourceArtefactId,
        string extractorName,
        string extractorVersion,
        IEnumerable<string> warnings,
        string extractedText)
        : base()
    {
        if (sourceArtefactId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Source artefact ID cannot be empty");
        if (string.IsNullOrWhiteSpace(extractorName) ||
            extractorName.Length > MaxExtractorNameLength ||
            extractorName.Any(char.IsControl) ||
            HasUnpairedSurrogate(extractorName))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Extractor name is required and cannot exceed {MaxExtractorNameLength} characters");
        }
        if (string.IsNullOrWhiteSpace(extractorVersion) ||
            extractorVersion.Length > MaxExtractorVersionLength ||
            extractorVersion.Any(char.IsControl) ||
            HasUnpairedSurrogate(extractorVersion))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Extractor version is required and cannot exceed {MaxExtractorVersionLength} characters");
        }
        if (extractedText is null)
            throw new DomainException(ErrorCodes.ValidationError, "Extracted text cannot be null");
        if (extractedText.Length > MaxExtractedTextLength)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Extracted text cannot exceed {MaxExtractedTextLength} characters");
        }
        if (extractedText.Contains('\r'))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Extracted text must use LF line endings");
        }
        if (HasUnpairedSurrogate(extractedText))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Extracted text must contain valid UTF-16");
        }
        if (warnings is null)
            throw new DomainException(ErrorCodes.ValidationError, "Extraction warnings cannot be null");

        var warningList = warnings.ToArray();
        if (warningList.Length > MaxWarningCount)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Extraction cannot contain more than {MaxWarningCount} warnings");
        }
        if (warningList.Any(warning =>
                string.IsNullOrWhiteSpace(warning) ||
                warning.Length > MaxWarningLength ||
                warning.Any(char.IsControl) ||
                HasUnpairedSurrogate(warning)))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Extraction warnings must be non-empty, control-free, and no longer than {MaxWarningLength} characters");
        }

        var warningsJson = JsonSerializer.Serialize(warningList);
        if (warningsJson.Length > MaxWarningsJsonLength)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Serialized extraction warnings cannot exceed {MaxWarningsJsonLength} characters");
        }

        SourceArtefactId = sourceArtefactId;
        ExtractorName = extractorName;
        ExtractorVersion = extractorVersion;
        WarningsJson = warningsJson;
        ExtractedText = extractedText;
        TextLength = extractedText.Length;
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    return true;
                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }
}
