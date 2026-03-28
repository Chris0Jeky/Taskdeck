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

            if (surface.HasValue && _settings.KilledSurfaces.Contains(surface.Value.ToString()))
                return Task.FromResult(true);

            if (userId.HasValue && _settings.KilledUserIds.Contains(userId.Value.ToString()))
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
                    _settings.GlobalKillReason = enabled ? reason : null;
                    break;

                case KillSwitchScope.Surface:
                    if (string.IsNullOrWhiteSpace(target))
                        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Target surface name is required"));
                    if (!Enum.TryParse<LlmSurface>(target, ignoreCase: true, out _))
                        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, $"Unknown surface: {target}"));

                    if (enabled)
                    {
                        _settings.KilledSurfaces.Add(target);
                        if (!string.IsNullOrWhiteSpace(reason))
                            _settings.Reasons[target] = reason;
                    }
                    else
                    {
                        _settings.KilledSurfaces.Remove(target);
                        _settings.Reasons.Remove(target);
                    }
                    break;

                case KillSwitchScope.Identity:
                    if (string.IsNullOrWhiteSpace(target))
                        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Target user ID is required"));
                    if (!Guid.TryParse(target, out _))
                        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Target must be a valid user ID"));

                    if (enabled)
                    {
                        _settings.KilledUserIds.Add(target);
                        if (!string.IsNullOrWhiteSpace(reason))
                            _settings.Reasons[target] = reason;
                    }
                    else
                    {
                        _settings.KilledUserIds.Remove(target);
                        _settings.Reasons.Remove(target);
                    }
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
            entries.Add(new KillSwitchEntryDto(KillSwitchScope.Global, null, _settings.GlobalKill, _settings.GlobalKillReason));

            foreach (var surface in _settings.KilledSurfaces)
            {
                _settings.Reasons.TryGetValue(surface, out var surfaceReason);
                entries.Add(new KillSwitchEntryDto(KillSwitchScope.Surface, surface, true, surfaceReason));
            }

            foreach (var userId in _settings.KilledUserIds)
            {
                _settings.Reasons.TryGetValue(userId, out var userReason);
                entries.Add(new KillSwitchEntryDto(KillSwitchScope.Identity, userId, true, userReason));
            }
        }

        return Task.FromResult(new KillSwitchStatusDto(
            _settings.GlobalKill,
            entries));
    }
}
