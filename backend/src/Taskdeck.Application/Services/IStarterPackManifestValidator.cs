using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

public sealed record StarterPackManifestValidationError(string Path, string Message);

public sealed class StarterPackManifestValidationResult
{
    public StarterPackManifestValidationResult(StarterPackManifestDto? manifest, IReadOnlyList<StarterPackManifestValidationError> errors)
    {
        Manifest = manifest;
        Errors = errors;
    }

    public StarterPackManifestDto? Manifest { get; }
    public IReadOnlyList<StarterPackManifestValidationError> Errors { get; }
    public bool IsValid => Errors.Count == 0;
}

public interface IStarterPackManifestValidator
{
    StarterPackManifestValidationResult ValidateJson(string manifestJson);
    StarterPackManifestValidationResult Validate(StarterPackManifestDto manifest);
}

