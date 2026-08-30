using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// The immutable text of an <see cref="Enums.SourceAssetStorageKind.InlineText"/>
/// <see cref="SourceAsset"/>, kept in its own row so asset-list reads never materialise up to
/// 200k characters per asset — the same split <see cref="SourceArtefact"/> / <see cref="ArtefactBlob"/>
/// use for binary content. Its primary key is the asset id (one-to-one).
/// </summary>
public sealed class SourceAssetTextPayload : Entity
{
    public Guid SourceAssetId { get; private set; }
    public string Text { get; private set; } = string.Empty;

    private SourceAssetTextPayload() : base()
    {
    }

    public SourceAssetTextPayload(Guid sourceAssetId, string text)
        : base(sourceAssetId)
    {
        if (sourceAssetId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Source asset ID cannot be empty");
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException(ErrorCodes.ValidationError, "Source asset text cannot be empty");
        if (text.Length > SourceAsset.MaxInlineTextLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Source asset text cannot exceed {SourceAsset.MaxInlineTextLength} characters");

        SourceAssetId = sourceAssetId;
        Text = text;
    }
}
