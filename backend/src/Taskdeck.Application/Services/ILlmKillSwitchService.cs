using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

public interface ILlmKillSwitchService
{
    Task<bool> IsKilledAsync(
        LlmSurface? surface = null,
        Guid? userId = null,
        CancellationToken ct = default);

    Task<Result> SetKillSwitchAsync(
        KillSwitchScope scope,
        string? target,
        bool enabled,
        string? reason,
        CancellationToken ct = default);

    Task<KillSwitchStatusDto> GetStatusAsync(CancellationToken ct = default);
}
