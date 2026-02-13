using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IOpsCliService
{
    Task<Result<CommandRunDto>> RunCommandAsync(Guid userId, RunCommandDto dto, CancellationToken ct = default);
    Task<Result<CommandRunDetailDto>> GetCommandRunAsync(Guid runId, CancellationToken ct = default);
    Task<Result<IEnumerable<CommandRunLogDto>>> GetCommandRunLogsAsync(Guid runId, CancellationToken ct = default);
    Result<IEnumerable<CommandTemplateDto>> GetAvailableTemplates();
}
