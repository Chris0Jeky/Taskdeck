namespace Taskdeck.Application.DTOs;

public sealed class StarterPackManifestDto
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string PackId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public StarterPackCompatibilityDto Compatibility { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<StarterPackLabelDto> Labels { get; set; } = new();
    public List<StarterPackColumnDto> Columns { get; set; } = new();
    public List<StarterPackCardTemplateDto> Templates { get; set; } = new();
    public List<StarterPackSeedCardDto> SeedCards { get; set; } = new();
}

public sealed class StarterPackCompatibilityDto
{
    public string MinTaskdeckVersion { get; set; } = string.Empty;
    public string? MaxTaskdeckVersion { get; set; }
    public List<string> RequiredFeatures { get; set; } = new();
}

public sealed class StarterPackLabelDto
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class StarterPackColumnDto
{
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public int? WipLimit { get; set; }
}

public sealed class StarterPackCardTemplateDto
{
    public string TemplateId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Checklist { get; set; } = new();
}

public sealed class StarterPackSeedCardDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string? TemplateId { get; set; }
    public List<string> Labels { get; set; } = new();
}
