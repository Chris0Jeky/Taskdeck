using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class AudioTranscriptionStore(TaskdeckDbContext db) : IAudioTranscriptionStore
{
    public Task<Result<AudioTranscriptionStatusDto>> StatusAsync(Guid userId, Guid audioId,
        SpeechTranscriptionConfiguration configuration, DateTimeOffset now, CancellationToken ct) => TransactionAsync(async () =>
    {
        _ = await OwnedAsync(userId, audioId, ct) ?? throw Missing();
        var attempts = await db.AudioTranscriptionAttempts.Where(x => x.UserId == userId && x.AudioAnswerId == audioId).ToListAsync(ct);
        foreach (var row in attempts) row.Expire(now);
        var budget = await db.AudioTranscriptionBudgets.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var today = budget?.UtcDay == Day(now);
        var receipts = new List<AudioTranscriptionReceiptDto>();
        foreach (var row in attempts.OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id)) receipts.Add(await MapAsync(row, ct));
        return new AudioTranscriptionStatusDto(configuration, today ? budget!.Attempts : 0, today ? budget!.InputBytes : 0, receipts);
    }, ct);

    public Task<Result<AudioTranscriptionAdmission>> AdmitAsync(Guid userId, Guid audioId, AudioTranscriptionRequestDto request,
        SpeechTranscriptionConfiguration configuration, DateTimeOffset now, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (request.RequestId == Guid.Empty || request.ExpectedRevision < 1 || request.ConfigurationHash is not { Length: 64 })
            throw Invalid("Reload this recording and confirm the configured transcription destination.");
        var owned = await OwnedAsync(userId, audioId, ct) ?? throw Missing();
        var hash = Representation.ComputeTextContentHash(JsonSerializer.Serialize(new { Schema = 1, AudioId = audioId, request.ExpectedRevision, request.ConfigurationHash }));
        var prior = await db.AudioTranscriptionAttempts.SingleOrDefaultAsync(x => x.UserId == userId && x.RequestId == request.RequestId, ct);
        if (prior is not null)
        {
            if (prior.RequestHash != hash || prior.AudioAnswerId != audioId) throw Conflict();
            prior.Expire(now); return new AudioTranscriptionAdmission(await MapAsync(prior, ct), null);
        }
        if (!configuration.Enabled) throw Invalid("Automatic transcription is not configured. The original remains available.");
        if (configuration.ConfigurationHash != request.ConfigurationHash || owned.Answer.Revision != request.ExpectedRevision) throw Conflict();
        var rows = await db.AudioTranscriptionAttempts.Where(x => x.UserId == userId && x.AudioAnswerId == audioId).ToListAsync(ct);
        foreach (var row in rows) row.Expire(now);
        if (rows.Any(x => x.State == ProcessingJobState.Running)) throw Conflict();
        if (rows.Count >= 20) throw Invalid("This recording has reached its 20-attempt limit. Keep or review its existing results.");
        var budget = await db.AudioTranscriptionBudgets.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (budget is null) { budget = new(userId); db.AudioTranscriptionBudgets.Add(budget); }
        if (!budget.Reserve(owned.Source.ByteSize, configuration.DailyAttempts, configuration.DailyInputBytes, now))
            throw Invalid("The daily transcription attempt or byte limit is reached. Existing recordings and transcripts remain available.");
        var attempt = new AudioTranscriptionAttempt(userId, audioId, owned.Capture.Id, owned.Source.Id,
            request.RequestId, hash, configuration.ConfigurationHash, configuration.Provider, configuration.Model, now, now.AddMinutes(2));
        db.AudioTranscriptionAttempts.Add(attempt);
        return new AudioTranscriptionAdmission(await MapAsync(attempt, ct),
            new(owned.Source.BlobReferenceId!.Value, owned.Source.ByteSize, owned.Source.ContentHash, owned.Source.MediaType));
    }, ct);

    public Task<Result<AudioTranscriptionReceiptDto>> FinishAsync(Guid userId, Guid attemptId, AudioTranscriptionProviderResult result,
        DateTimeOffset now, CancellationToken ct) => TransactionAsync(async () =>
    {
        var attempt = await db.AudioTranscriptionAttempts.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == attemptId, ct) ?? throw Missing();
        var owned = await OwnedAsync(userId, attempt.AudioAnswerId, ct);
        if (owned is null)
        {
            if (attempt.State == ProcessingJobState.Running) attempt.Fail("access-changed", now);
            // No transcript can be returned or staged after source access disappears.
            return Receipt(attempt, null);
        }
        if (attempt.Expire(now) || attempt.State != ProcessingJobState.Running) return await MapAsync(attempt, ct);
        if (owned.Capture.Id != attempt.CaptureId || owned.Source.Id != attempt.SourceAssetId) throw Conflict();
        if (result.FailureCode is not null) { attempt.Fail(result.FailureCode, now); return Receipt(attempt, null); }
        if (string.IsNullOrWhiteSpace(result.Text) || result.Text.Length > HttpAudioTranscriptionProvider.MaximumTextLength)
        { attempt.Fail("provider-response", now); return Receipt(attempt, null); }
        var payload = new Transcript(userId, CaptureSource.Voice, result.Text, boardId: owned.Answer.BoardId, createdFromCaptureId: attempt.CaptureId);
        var header = new Representation(payload.Id, attempt.CaptureId, userId, RepresentationKind.Transcript,
            attempt.SourceAssetId, null, attempt.Id, attempt.Provider, "1", attempt.Model, attempt.ConfigurationHash, 1,
            Representation.ComputeTextContentHash(payload.Text), null, RepresentationQualityState.Provisional,
            ["Automatic transcript; review against the original before using it as an answer."], now);
        header.ValidateParent(owned.Source, owned.Capture); header.ValidatePayload(payload);
        db.Transcripts.Add(payload); db.Representations.Add(header); attempt.Complete(header.Id, now);
        // ThinkingAudioAnswer is deliberately untracked: a processor never replaces a written/confirmed answer.
        return Receipt(attempt, payload.Text);
    }, ct);

    public async Task DeleteOwnerAsync(Guid userId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Transcription erasure requires the account transaction.");
        await db.AudioTranscriptionAttempts.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.AudioTranscriptionBudgets.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public async Task<bool> DispatchAllowedAsync(Guid userId, Guid attemptId, DateTimeOffset now, CancellationToken ct)
    {
        var attempt = await db.AudioTranscriptionAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct);
        if (attempt is null || attempt.State != ProcessingJobState.Running || now >= attempt.Deadline) return false;
        var owned = await OwnedAsync(userId, attempt.AudioAnswerId, ct);
        return owned is not null && owned.Source.Id == attempt.SourceAssetId && owned.Capture.Id == attempt.CaptureId;
    }

    private async Task<OwnedRecording?> OwnedAsync(Guid userId, Guid audioId, CancellationToken ct)
    {
        var answer = await db.ThinkingAudioAnswers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == audioId
            && db.Users.Any(user => user.Id == userId && user.IsActive)
            && (x.BoardId == null || db.Boards.Any(board => board.Id == x.BoardId && (board.OwnerId == userId
                || db.BoardAccesses.Any(access => access.BoardId == board.Id && access.UserId == userId)))), ct);
        if (answer is null) return null;
        var capture = await db.Captures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == answer.CaptureId && x.UserId == userId, ct);
        var source = await db.SourceAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == answer.SourceAssetId && x.CaptureId == answer.CaptureId, ct);
        return capture is null || source is null || source.BlobReferenceId is null || source.ByteSize is <= 0 or > ThinkingAudioService.MaximumBytes
            ? null : new(answer, capture, source);
    }
    private sealed record OwnedRecording(ThinkingAudioAnswer Answer, Capture Capture, SourceAsset Source);
    private async Task<AudioTranscriptionReceiptDto> MapAsync(AudioTranscriptionAttempt attempt, CancellationToken ct)
    {
        var text = attempt.RepresentationId is { } id
            ? await db.Transcripts.AsNoTracking().Where(x => x.UserId == attempt.UserId && x.Id == id).Select(x => x.Text).SingleOrDefaultAsync(ct) : null;
        return Receipt(attempt, text);
    }
    private static AudioTranscriptionReceiptDto Receipt(AudioTranscriptionAttempt row, string? text) => new(row.Id, row.RequestId,
        row.AudioAnswerId, row.State.ToString(), row.Provider, row.Model, row.ConfigurationHash, row.StartedAt, row.Deadline,
        row.FinishedAt, row.FailureCode, row.RepresentationId, text);
    private async Task<Result<T>> TransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var value = await action(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return Result.Success(value);
        }
        catch (DomainException ex) { return Result.Failure<T>(ex.ErrorCode, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result.Failure<T>(ErrorCodes.Conflict, Conflict().Message); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 5 or 6 or 19 })
        { return Result.Failure<T>(ErrorCodes.Conflict, Conflict().Message); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        { return Result.Failure<T>(ErrorCodes.Conflict, Conflict().Message); }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().Where(x => x.Entity is AudioTranscriptionAttempt or AudioTranscriptionBudget or Transcript or Representation).ToArray())
                entry.State = EntityState.Detached;
        }
    }
    private static int Day(DateTimeOffset now) => now.UtcDateTime.Year * 10000 + now.UtcDateTime.Month * 100 + now.UtcDateTime.Day;
    private static DomainException Missing() => new(ErrorCodes.NotFound, "This private recording is unavailable.");
    private static DomainException Conflict() => new(ErrorCodes.Conflict, "The recording, destination or attempt changed. Reload its receipts before requesting transcription again.");
    private static DomainException Invalid(string message) => new(ErrorCodes.ValidationError, message);
}
