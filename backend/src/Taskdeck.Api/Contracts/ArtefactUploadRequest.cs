namespace Taskdeck.Api.Contracts;

/// <summary>
/// Multipart contract for a source artefact upload. Provenance source is assigned
/// server-side; callers cannot claim a trusted connector identity.
/// </summary>
public sealed class ArtefactUploadRequest
{
    public IFormFile? File { get; set; }
    public Guid? BoardId { get; set; }
    public Guid? CreatedFromCaptureId { get; set; }
}
