using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class ArtefactStorageSettings
{
    public const long DefaultMaxBytesPerArtefact = 10L * 1024 * 1024;
    public const long DefaultMaxBytesPerUser = 200L * 1024 * 1024;

    [Range(1, int.MaxValue)]
    public long MaxBytesPerArtefact { get; set; } = DefaultMaxBytesPerArtefact;

    [Range(1, int.MaxValue)]
    public long MaxBytesPerUser { get; set; } = DefaultMaxBytesPerUser;
}
