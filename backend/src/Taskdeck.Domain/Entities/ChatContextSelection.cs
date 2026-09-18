using Taskdeck.Domain.Exceptions;
namespace Taskdeck.Domain.Entities;

public sealed record ChatMemoryReference(Guid Id, int Revision);
public sealed record ChatAssetReference(Guid MemoryId, int Revision, Guid AssetId, string ContentHash);
public sealed record ChatContextSource(string Kind, Guid Id, string Title, long? Revision, bool Truncated,
    Guid? MemoryId = null, string? ContentHash = null, Guid? SupersededByAssetId = null);
public sealed record ChatContextSelection(Guid? CardId, bool IncludeThinking, IReadOnlyList<ChatMemoryReference> Memories,
    IReadOnlyList<ChatAssetReference>? Assets = null)
{
    public void Validate()
    {
        if (CardId == Guid.Empty || IncludeThinking && !CardId.HasValue || Memories is null || Memories.Count > 5 ||
            Memories.Any(memory => memory is null || memory.Id == Guid.Empty || memory.Revision < 1) ||
            Memories.Select(memory => memory.Id).Distinct().Count() != Memories.Count ||
            (Assets?.Count ?? 0) + Memories.Count > 5 ||
            Assets is not null && (Assets.Any(asset => asset is null || asset.MemoryId == Guid.Empty || asset.AssetId == Guid.Empty ||
                asset.Revision < 1 || asset.ContentHash is null || asset.ContentHash.Length != 64 ||
                asset.ContentHash.Any(character => !Uri.IsHexDigit(character))) ||
                Assets.Select(asset => asset.AssetId).Distinct().Count() != Assets.Count))
            throw new DomainException(ErrorCodes.ValidationError, "Choose one card and at most five distinct, saved private memories or original sources.");
    }
}
