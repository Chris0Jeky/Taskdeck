using System.Text.Json;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

public sealed class StarterPackManifestValidator : IStarterPackManifestValidator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly StarterPackSchemaValidator _schemaValidator;
    private readonly StarterPackSemanticValidator _semanticValidator;

    public StarterPackManifestValidator()
        : this(new StarterPackSchemaValidator(), new StarterPackSemanticValidator())
    {
    }

    public StarterPackManifestValidator(
        StarterPackSchemaValidator schemaValidator,
        StarterPackSemanticValidator semanticValidator)
    {
        _schemaValidator = schemaValidator;
        _semanticValidator = semanticValidator;
    }

    public StarterPackManifestValidationResult ValidateJson(string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return new StarterPackManifestValidationResult(
                null,
                new[] { new StarterPackManifestValidationError("$", "Manifest JSON cannot be empty.") });
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<StarterPackManifestDto>(manifestJson, SerializerOptions);
            if (manifest == null)
            {
                return new StarterPackManifestValidationResult(
                    null,
                    new[] { new StarterPackManifestValidationError("$", "Manifest JSON could not be parsed.") });
            }

            return Validate(manifest);
        }
        catch (JsonException ex)
        {
            return new StarterPackManifestValidationResult(
                null,
                new[] { new StarterPackManifestValidationError("$", $"Manifest JSON is invalid: {ex.Message}") });
        }
    }

    public StarterPackManifestValidationResult Validate(StarterPackManifestDto manifest)
    {
        var errors = new List<StarterPackManifestValidationError>();
        if (manifest == null)
        {
            errors.Add(new StarterPackManifestValidationError("$", "Manifest cannot be null."));
            return new StarterPackManifestValidationResult(null, errors);
        }

        var schemaOutput = _schemaValidator.Validate(manifest, errors);
        _semanticValidator.Validate(schemaOutput, errors);

        return new StarterPackManifestValidationResult(manifest, errors);
    }
}
