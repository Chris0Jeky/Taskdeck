using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IDailySealService
{
    Task<Result<DailySealResponse>> SealDayAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default);
    Task<Result<DailySealStatusResponse>> GetSealStatusAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default);
}

public sealed record DailySealResponse(DateTimeOffset SealedAt, bool WasAlreadySealed);
public sealed record DailySealStatusResponse(DateOnly Date, bool IsSealed, DateTimeOffset? SealedAt);
