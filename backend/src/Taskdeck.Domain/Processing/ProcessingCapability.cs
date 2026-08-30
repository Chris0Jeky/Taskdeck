namespace Taskdeck.Domain.Processing;

/// <summary>
/// The capability vocabulary of the Context Fabric processing layer (ADR-0065 §Decision 6).
/// Pipelines request a capability; they never name a worker class, a capture-source enum, or a
/// request-type prefix. Processor manifests may declare only capabilities listed here.
/// </summary>
public static class ProcessingCapability
{
    public const string ContentInspect = "content.inspect";
    public const string TextNormalize = "text.normalize";
    public const string DocumentExtractText = "document.extract-text";
    public const string ImageOcr = "image.ocr";
    public const string ImageDescribe = "image.describe";
    public const string AudioPreprocess = "audio.preprocess";
    public const string AudioTranscribe = "audio.transcribe";
    public const string AudioAlign = "audio.align";
    public const string AudioDiarize = "audio.diarize";
    public const string SemanticExtract = "semantic.extract";
    public const string ContextResolve = "context.resolve";
    public const string ChangePlan = "change.plan";
    public const string ChangeVerify = "change.verify";

    /// <summary>Every known capability, in declaration order.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        ContentInspect,
        TextNormalize,
        DocumentExtractText,
        ImageOcr,
        ImageDescribe,
        AudioPreprocess,
        AudioTranscribe,
        AudioAlign,
        AudioDiarize,
        SemanticExtract,
        ContextResolve,
        ChangePlan,
        ChangeVerify
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>Capabilities that derive representations and therefore may never touch work state.</summary>
    public static readonly IReadOnlyList<string> RepresentationProducing = new[]
    {
        TextNormalize,
        DocumentExtractText,
        ImageOcr,
        ImageDescribe,
        AudioPreprocess,
        AudioTranscribe,
        AudioAlign,
        AudioDiarize
    };

    public static bool IsKnown(string? capability) =>
        capability is not null && Known.Contains(capability);

    /// <summary>
    /// Returns the capability's domain prefix (<c>audio</c> in <c>audio.transcribe</c>), or null when
    /// the value is not a known capability.
    /// </summary>
    public static string? DomainOf(string? capability)
    {
        if (!IsKnown(capability))
        {
            return null;
        }

        var separator = capability!.IndexOf('.', StringComparison.Ordinal);
        return separator <= 0 ? null : capability[..separator];
    }
}
