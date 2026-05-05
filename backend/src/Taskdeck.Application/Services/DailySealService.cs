using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class DailySealService : IDailySealService
{
    private readonly IUnitOfWork _unitOfWork;

    public DailySealService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DailySealResponse>> SealDayAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<DailySealResponse>(ErrorCodes.ValidationError, "UserId cannot be empty");

        var now = DateTimeOffset.UtcNow;

        if (date > DateOnly.FromDateTime(now.UtcDateTime))
            return Result.Failure<DailySealResponse>(ErrorCodes.ValidationError, "Cannot seal a future date");

        var snapshot = await _unitOfWork.DailySnapshots.GetByUserAndDateAsync(userId, date, cancellationToken);

        var wasAlreadySealed = false;

        if (snapshot is null)
        {
            snapshot = new DailySnapshot(userId, date, now);
            snapshot.Seal(now);
            await _unitOfWork.DailySnapshots.AddAsync(snapshot, cancellationToken);
        }
        else
        {
            wasAlreadySealed = snapshot.IsSealed;
            snapshot.Seal(now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Re-fetch to ensure accurate response after potential concurrent seal resolution.
        // If TryResolveDuplicateDailySnapshotConflicts detached our entity, the persisted
        // snapshot will have a different Id — meaning another request won the race.
        var persisted = await _unitOfWork.DailySnapshots.GetByUserAndDateAsync(userId, date, cancellationToken);
        if (persisted != null && persisted.Id != snapshot.Id)
        {
            return Result.Success(new DailySealResponse(persisted.SealedAt!.Value, WasAlreadySealed: true));
        }

        return Result.Success(new DailySealResponse(snapshot.SealedAt!.Value, wasAlreadySealed));
    }

    public async Task<Result<DailySealStatusResponse>> GetSealStatusAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<DailySealStatusResponse>(ErrorCodes.ValidationError, "UserId cannot be empty");

        var snapshot = await _unitOfWork.DailySnapshots.GetByUserAndDateAsync(userId, date, cancellationToken);

        if (snapshot is null)
            return Result.Success(new DailySealStatusResponse(date, false, null));

        return Result.Success(new DailySealStatusResponse(date, snapshot.IsSealed, snapshot.SealedAt));
    }
}
