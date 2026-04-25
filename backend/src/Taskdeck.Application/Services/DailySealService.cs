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

    public async Task<Result<DailySealResponse>> SealDayAsync(Guid userId, DateOnly date)
    {
        if (userId == Guid.Empty)
            return Result.Failure<DailySealResponse>(ErrorCodes.ValidationError, "UserId cannot be empty");

        var now = DateTimeOffset.UtcNow;

        if (date > DateOnly.FromDateTime(now.UtcDateTime))
            return Result.Failure<DailySealResponse>(ErrorCodes.ValidationError, "Cannot seal a future date");

        var snapshot = await _unitOfWork.DailySnapshots.GetByUserAndDateAsync(userId, date);

        var wasAlreadySealed = false;

        if (snapshot is null)
        {
            snapshot = new DailySnapshot(userId, date, now);
            snapshot.Seal(now);
            await _unitOfWork.DailySnapshots.AddAsync(snapshot);
        }
        else
        {
            wasAlreadySealed = snapshot.IsSealed;
            snapshot.Seal(now);
        }

        await _unitOfWork.SaveChangesAsync();

        return Result.Success(new DailySealResponse(snapshot.SealedAt!.Value, wasAlreadySealed));
    }

    public async Task<Result<DailySealStatusResponse>> GetSealStatusAsync(Guid userId, DateOnly date)
    {
        if (userId == Guid.Empty)
            return Result.Failure<DailySealStatusResponse>(ErrorCodes.ValidationError, "UserId cannot be empty");

        var snapshot = await _unitOfWork.DailySnapshots.GetByUserAndDateAsync(userId, date);

        if (snapshot is null)
            return Result.Success(new DailySealStatusResponse(date, false, null));

        return Result.Success(new DailySealStatusResponse(date, snapshot.IsSealed, snapshot.SealedAt));
    }
}
