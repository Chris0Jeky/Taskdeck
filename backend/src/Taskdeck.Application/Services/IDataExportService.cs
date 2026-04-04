using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service for exporting all user data in a structured, versioned format
/// for GDPR-style data portability.
/// </summary>
public interface IDataExportService
{
    /// <summary>
    /// Export all data belonging to the specified user as a versioned JSON-serializable package.
    /// The export is scoped strictly to the requesting user's data.
    /// This method buffers up to the configured row limit per entity type; use
    /// <see cref="StreamUserDataExportAsync"/> for complete exports of large datasets.
    /// </summary>
    Task<Result<UserDataExportDto>> ExportUserDataAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream all data belonging to the specified user as a versioned JSON export written
    /// directly to <paramref name="destination"/> using <see cref="System.Text.Json.Utf8JsonWriter"/>.
    /// Removes the 10 k row hard cap and fixes the N+1 chat-session message-count query.
    /// The on-wire format is identical to <see cref="ExportUserDataAsync"/>; the only
    /// behavioural difference is that rows are never truncated.
    /// </summary>
    Task<Result> StreamUserDataExportAsync(Guid userId, Stream destination, CancellationToken cancellationToken = default);
}
