using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class TomorrowNoteService : ITomorrowNoteService
{
    private readonly ITomorrowNoteRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public TomorrowNoteService(ITomorrowNoteRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TomorrowNoteResponse?>> GetNoteAsync(
        Guid userId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<TomorrowNoteResponse?>(ErrorCodes.ValidationError, "User ID is required");

        var note = await _repository.GetByUserAndDateAsync(userId, date, cancellationToken);
        if (note is null)
            return Result.Success<TomorrowNoteResponse?>(null);

        return Result.Success<TomorrowNoteResponse?>(MapToResponse(note));
    }

    public async Task<Result<TomorrowNoteResponse>> SaveNoteAsync(
        Guid userId,
        DateOnly date,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<TomorrowNoteResponse>(ErrorCodes.ValidationError, "User ID is required");

        if (text is null)
            return Result.Failure<TomorrowNoteResponse>(ErrorCodes.ValidationError, "Text cannot be null");

        if (text.Length > TomorrowNote.MaxTextLength)
            return Result.Failure<TomorrowNoteResponse>(
                ErrorCodes.ValidationError,
                $"Text cannot exceed {TomorrowNote.MaxTextLength} characters");

        var existing = await _repository.GetByUserAndDateAsync(userId, date, cancellationToken);
        if (existing is not null)
        {
            existing.UpdateText(text);
            await _repository.UpdateAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToResponse(existing));
        }

        var note = new TomorrowNote(userId, date, text);
        await _repository.AddAsync(note, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(MapToResponse(note));
    }

    private static TomorrowNoteResponse MapToResponse(TomorrowNote note)
    {
        return new TomorrowNoteResponse(
            note.Id,
            note.Date,
            note.Text,
            note.UpdatedAt,
            note.CreatedAt);
    }
}
