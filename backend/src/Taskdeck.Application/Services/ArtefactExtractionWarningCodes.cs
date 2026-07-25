namespace Taskdeck.Application.Services;

/// <summary>
/// Stable, content-free warning codes persisted with extraction history.
/// </summary>
public static class ArtefactExtractionWarningCodes
{
    public const string NoTextLayer = "no-text-layer";
    public const string PageLimit = "page-limit";
    public const string CharacterLimit = "character-limit";
    public const string InputTooLarge = "input-too-large";
    public const string InvalidUtf8 = "invalid-utf8";
    public const string InvalidText = "invalid-text";
    public const string ExtractorError = "extractor-error";
    public const string ExtractorContractError = "extractor-contract-error";
    public const string ExtractionTimeout = "extraction-timeout";
}
