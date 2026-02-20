using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

public interface IStarterPackCatalogService
{
    IReadOnlyList<StarterPackCatalogEntryDto> GetCatalog();
}
