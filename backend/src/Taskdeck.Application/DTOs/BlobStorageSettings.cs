namespace Taskdeck.Application.DTOs;

public sealed class BlobStorageSettings
{
    public long MaximumUploadBytes { get; set; } = 64L * 1024 * 1024;
    public long OwnerQuotaBytes { get; set; } = 256L * 1024 * 1024;
    public long ModalityQuotaBytes { get; set; } = 128L * 1024 * 1024;
    public int MaximumReferencesPerOwner { get; set; } = 10_000;
}
