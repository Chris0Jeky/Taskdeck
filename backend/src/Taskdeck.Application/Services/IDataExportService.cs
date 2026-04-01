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
    /// </summary>
    Task<Result<UserDataExportDto>> ExportUserDataAsync(Guid userId, CancellationToken cancellationToken = default);
}
