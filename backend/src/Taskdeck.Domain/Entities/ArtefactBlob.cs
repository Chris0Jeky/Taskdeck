using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Cold binary payload stored separately from <see cref="SourceArtefact"/> metadata.
/// Its primary key is also the source artefact foreign key (one-to-one).
/// </summary>
public sealed class ArtefactBlob : Entity
{
    public Guid SourceArtefactId { get; private set; }
    public byte[] Content { get; private set; } = [];

    private ArtefactBlob() : base()
    {
    }

    public ArtefactBlob(Guid sourceArtefactId, byte[] content)
        : base(sourceArtefactId)
    {
        if (sourceArtefactId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Source artefact ID cannot be empty");
        if (content.Length == 0)
            throw new DomainException(ErrorCodes.ValidationError, "Artefact content cannot be empty");

        SourceArtefactId = sourceArtefactId;
        Content = content.ToArray();
    }
}
