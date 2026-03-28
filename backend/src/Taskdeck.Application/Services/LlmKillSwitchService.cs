using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class LlmKillSwitchService : ILlmKillSwitchService
{
    private readonly LlmKillSwitchSettings _settings;
    private readonly object _lock = new();

    public LlmKillSwitchService(LlmKillSwitchSettings settings)
    {
        _settings = settings;
    }

    public Task<bool> IsKilledAsync(
        LlmSurface? surface = null,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_settings.GlobalKill)
                return Task.FromResult(true);

            if (surface.HasValue &&
                _settings.KilledSurfaces.Contains(surface.Value.ToString(), StringComparer.OrdinalIgnoreCase))
                return Task.FromResult(true);

            if (userId.HasValue &&
                _settings.KilledUserIds.Contains(userId.Value.ToString(), StringComparer.OrdinalIgnoreCase))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<Result> SetKillSwitchAsync(
        KillSwitchScope scope,
        string? target,
        bool enabled,
        string? reason,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            switch (scope)
            {
                case KillSwitchScope.Global:
                    _settings.GlobalKill = enabled;
                    break;

                case KillSwitchScope.Surface:
                    if (string.IsNullOrWhiteSpace(target))
                        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Target surface name is required"));
                    if (!Enum.TryParse<LlmSurface>(target, ignoreCase: true, out _))
                        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, $"Unknown surface: {target}"));

                    if (enabled && !_settings.KilledSurfaces.Contains(target, StringComparer.OrdinalIgnoreCase))
                        _settings.KilledSurfaces.Add(target);
                    else if (!enabled)
                        _settings.KilledSurfaces.RemoveAll(s => s.Equals(target, StringComparison.OrdinalIgnoreCase));
                    break;

                case KillSwitchScope.Identity:
                    if (string.IsNullOrWhiteSpace(target))
                        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Target user ID is required"));
                    if (!Guid.TryParse(target, out _))
                        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Target must be a valid user ID"));

                    if (enabled && !_settings.KilledUserIds.Contains(target, StringComparer.OrdinalIgnoreCase))
                        _settings.KilledUserIds.Add(target);
                    else if (!enabled)
                        _settings.KilledUserIds.RemoveAll(u => u.Equals(target, StringComparison.OrdinalIgnoreCase));
                    break;

                default:
                    return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, $"Unknown scope: {scope}"));
            }
        }

        return Task.FromResult(Result.Success());
    }

    public Task<KillSwitchStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var entries = new List<KillSwitchEntryDto>();

        lock (_lock)
        {
            entries.Add(new KillSwitchEntryDto(KillSwitchScope.Global, null, _settings.GlobalKill, null));

            foreach (var surface in _settings.KilledSurfaces)
            {
                entries.Add(new KillSwitchEntryDto(KillSwitchScope.Surface, surface, true, null));
            }

            foreach (var userId in _settings.KilledUserIds)
            {
                entries.Add(new KillSwitchEntryDto(KillSwitchScope.Identity, userId, true, null));
            }
        }

        return Task.FromResult(new KillSwitchStatusDto(
            _settings.GlobalKill,
            entries));
    }
}
