using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IDailySealService
{
    Task<Result<DailySealResponse>> SealDayAsync(Guid userId, DateOnly date);
    Task<Result<DailySealStatusResponse>> GetSealStatusAsync(Guid userId, DateOnly date);
}

public sealed record DailySealResponse(DateTimeOffset SealedAt, bool WasAlreadySealed);
public sealed record DailySealStatusResponse(DateOnly Date, bool IsSealed, DateTimeOffset? SealedAt);
