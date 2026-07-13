using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class ArtefactStorageSettings
{
    public const long DefaultMaxBytesPerArtefact = 10L * 1024 * 1024;
    public const long DefaultMaxBytesPerUser = 200L * 1024 * 1024;

    [Range(typeof(long), "1", "2147483647")]
    public long MaxBytesPerArtefact { get; set; } = DefaultMaxBytesPerArtefact;

    [Range(typeof(long), "1", "9223372036854775807")]
    public long MaxBytesPerUser { get; set; } = DefaultMaxBytesPerUser;
}
