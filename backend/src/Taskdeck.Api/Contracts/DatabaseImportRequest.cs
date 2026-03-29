namespace Taskdeck.Api.Contracts;

/// <summary>
/// Wraps the IFormFile upload for database import. Using a class wrapper is required
/// by Swashbuckle 6.9+ to correctly generate the OpenAPI schema for file uploads.
/// </summary>
public sealed class DatabaseImportRequest
{
    public IFormFile? File { get; set; }
}
