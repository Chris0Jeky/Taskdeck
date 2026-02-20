namespace Taskdeck.Application.DTOs;

public static class StarterPackCatalogCategories
{
    public const string LabelPack = "label-pack";
    public const string ColumnFlow = "column-flow";
    public const string BoardBlueprint = "board-blueprint";
}

public record StarterPackCatalogEntryDto(
    string Id,
    string Category,
    string Title,
    string Summary,
    List<string> Highlights,
    StarterPackManifestDto Manifest
);
