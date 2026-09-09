using Taskdeck.Domain.Exceptions;
namespace Taskdeck.Domain.Entities;

public sealed record ChatMemoryReference(Guid Id, int Revision);
public sealed record ChatContextSource(string Kind, Guid Id, string Title, long? Revision, bool Truncated);
public sealed record ChatContextSelection(Guid? CardId, bool IncludeThinking, IReadOnlyList<ChatMemoryReference> Memories)
{
    public void Validate()
    {
        if (CardId == Guid.Empty || IncludeThinking && !CardId.HasValue || Memories is null || Memories.Count > 5 ||
            Memories.Any(memory => memory is null || memory.Id == Guid.Empty || memory.Revision < 1) ||
            Memories.Select(memory => memory.Id).Distinct().Count() != Memories.Count)
            throw new DomainException(ErrorCodes.ValidationError, "Choose one card and at most five distinct, saved private memories.");
    }
}
