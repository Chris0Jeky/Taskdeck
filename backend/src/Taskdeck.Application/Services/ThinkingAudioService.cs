using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class ThinkingAudioService(IUnitOfWork work, IThinkingDeckRepository decks,
    IThinkingAudioRepository answers, IAuthorizationService authorization, ICaptureStore captures,
    IBlobStore blobs, IManualRepresentationStore representations, ThinkingAnswerService writtenAnswers)
{
    public const long MaximumBytes = 2 * 1024 * 1024;
    private const int MaximumWrittenVersions = 50;

    public async Task<Result<ThinkingAudioLibraryPage>> LibraryAsync(Guid userId, int offset, CancellationToken ct)
    {
        const int pageSize = 20;
        if (offset < 0 || offset > int.MaxValue - pageSize - 1)
            return Result.Failure<ThinkingAudioLibraryPage>(ErrorCodes.ValidationError, "The recording page is invalid.");
        var candidates = await answers.ListByUserAsync(userId, offset, pageSize + 1, ct);
        var allowed = await authorization.GetReadableBoardIdsAsync(userId,
            candidates.Where(x => x.BoardId.HasValue).Select(x => x.BoardId!.Value), ct);
        if (!allowed.IsSuccess) return Result.Failure<ThinkingAudioLibraryPage>(allowed.ErrorCode, allowed.ErrorMessage);
        var visibleIds = candidates.Take(pageSize).Where(x => x.BoardId is null || allowed.Value.Contains(x.BoardId.Value))
            .Select(x => x.Id).ToArray();
        var items = await answers.LibraryEntriesAsync(userId, visibleIds, ct);
        // Advance over the bounded owner page even when revoked boards hide every candidate.
        return Result.Success(new ThinkingAudioLibraryPage(items, candidates.Count > pageSize ? offset + pageSize : null));
    }

    public async Task<Result<ThinkingAudioLibraryDetail>> LibraryDetailAsync(Guid userId, Guid id, CancellationToken ct)
    {
        try
        {
            var answer = await LibraryOwnedAsync(userId, id, ct);
            Guid? boardId = null; Guid? cardId = null;
            if (answer.BoardId is { } currentBoardId)
            {
                try
                {
                    var (_, question) = await QuestionAsync(userId, currentBoardId, answer.CardId, answer.LayerId, ct);
                    if (Hash(question) == answer.QuestionHash) { boardId = currentBoardId; cardId = answer.CardId; }
                }
                catch (DomainException ex) when (ex.ErrorCode == ErrorCodes.NotFound) { }
            }
            return Result.Success(new ThinkingAudioLibraryDetail(await MapAsync(answer, ct), boardId, cardId));
        }
        catch (DomainException ex) { return Result.Failure<ThinkingAudioLibraryDetail>(ex.ErrorCode, ex.Message); }
    }

    public async Task<Result<ThinkingAudioDownload>> LibraryDownloadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        try { return Result.Success(await OpenOriginalAsync(await LibraryOwnedAsync(userId, id, ct), ct)); }
        catch (DomainException ex) { return Result.Failure<ThinkingAudioDownload>(ex.ErrorCode, ex.Message); }
    }

    private async Task<ThinkingAudioAnswer> LibraryOwnedAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var answer = await answers.GetAsync(userId, id, ct) ?? throw Missing();
        if (answer.BoardId is { } boardId)
        {
            var allowed = await authorization.CanReadBoardAsync(userId, boardId);
            if (!allowed.IsSuccess || !allowed.Value) throw Missing();
        }
        return answer;
    }

    public async Task<Result<ThinkingAudioDto?>> GetQuestionAsync(Guid userId, Guid boardId, Guid cardId, Guid layerId, CancellationToken ct)
    {
        try
        {
            var (_, layer) = await QuestionAsync(userId, boardId, cardId, layerId, ct);
            var answer = await answers.QuestionAsync(userId, cardId, layerId, Hash(layer), ct);
            return Result.Success(answer is null ? null : await MapAsync(answer, ct));
        }
        catch (DomainException ex) { return Result.Failure<ThinkingAudioDto?>(ex.ErrorCode, ex.Message); }
    }

    public Task<Result<ThinkingAudioDto>> UploadAsync(Guid userId, Guid boardId, Guid cardId, Guid layerId,
        ThinkingAudioUploadDto dto, string mediaType, Stream content, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (dto.UploadId == Guid.Empty || dto.ByteSize is <= 0 or > MaximumBytes || dto.FileName is null
            || dto.FileName.Length is 0 or > 200 || dto.FileName.Any(char.IsControl) || dto.FileName.IndexOfAny(['/', '\\']) >= 0)
            throw Invalid("Choose an audio file up to 2 MiB with a plain file name.");
        mediaType = mediaType.Split(';')[0].Trim().ToLowerInvariant();
        if (mediaType is not ("audio/webm" or "audio/ogg" or "audio/wav" or "audio/x-wav" or "audio/mpeg" or "audio/mp4"))
            throw Invalid("Choose WebM, Ogg, WAV, MP3 or MP4 audio.");
        var (deck, layer) = await QuestionAsync(userId, boardId, cardId, layerId, ct);
        var hash = Hash(layer);
        var prior = await answers.UploadAsync(userId, dto.UploadId, ct);
        if (prior is not null)
        {
            if (prior.BoardId != boardId || prior.CardId != cardId || prior.LayerId != layerId || prior.QuestionHash != hash) throw Conflict();
            var saved = await MapAsync(prior, ct);
            if (saved.ByteSize != dto.ByteSize || saved.MediaType != mediaType || saved.FileName != dto.FileName
                || saved.ContentHash != await HashUploadAsync(content, dto.ByteSize, ct)) throw Conflict();
            return saved;
        }
        if (deck.Revision != dto.ExpectedDeckRevision || await answers.QuestionAsync(userId, cardId, layerId, hash, ct) is not null) throw Conflict();
        var blob = await blobs.AcquireAsync(new(userId, CaptureModality.Audio, dto.ByteSize, "thinking-audio-upload", dto.UploadId), content, ct);
        await using (var original = await blobs.OpenReferenceReadAsync(blob.ReferenceId, userId, ct))
        {
            var prefix = new byte[12]; var read = 0;
            while (read < prefix.Length)
            {
                var count = await original!.ReadAsync(prefix.AsMemory(read), ct); if (count == 0) break; read += count;
            }
            if (!LooksLikeAudio(prefix, read, mediaType)) throw Invalid("The file header does not match this audio format.");
        }
        var title = string.IsNullOrWhiteSpace(layer.Title) ? "Thinking question" : layer.Title;
        var (capture, asset) = await new CaptureIntakeService(captures, null).StageAudioAnswerAsync(userId, boardId,
            title.Length > 240 ? title[..240] : title, $"Thinking question: {layer.Title}\n{layer.Body}", blob, mediaType, dto.FileName, ct);
        var answer = new ThinkingAudioAnswer(userId, boardId, cardId, layerId, hash, capture.Id, asset.Id, dto.UploadId);
        answers.Add(answer); decks.GuardRevision(deck);
        (await work.Boards.GetByIdAsync(boardId, ct))!.RecordDependentMutation();
        if (!await answers.SaveAsync(ct)) throw Conflict();
        return await MapAsync(answer, ct);
    }, ct);

    public Task<Result<ThinkingAudioDto>> WriteAsync(Guid userId, Guid id, ThinkingAudioWriteDto dto, CancellationToken ct) => TransactionAsync(async () =>
    {
        var answer = await OwnedAsync(userId, id, ct);
        if (answer.ConfirmedMemoryId.HasValue) throw Conflict();
        var (_, question) = await QuestionAsync(userId, answer.BoardId!.Value, answer.CardId, answer.LayerId, ct);
        if (Hash(question) != answer.QuestionHash) throw Conflict();
        if (string.IsNullOrWhiteSpace(dto.Text) || dto.Text.Length > 8000) throw Invalid("Write between 1 and 8,000 characters.");
        var payload = new Transcript(userId, CaptureSource.Typed, dto.Text, boardId: answer.BoardId, createdFromCaptureId: answer.CaptureId);
        var previous = answer.RepresentationId is { } previousId ? await representations.HeaderAsync(previousId, userId, ct) : null;
        if (previous is not null && previous.ContentHash == Representation.ComputeTextContentHash(payload.Text)) return await MapAsync(answer, ct);
        if (answer.Revision != dto.ExpectedRevision) throw Conflict();
        if ((await representations.ListByCaptureAsync(answer.CaptureId, userId, ct)).Count >= MaximumWrittenVersions - 1)
            throw Invalid("This recording has reached its written-version limit. Confirm the current version or use a text answer.");
        var header = Header(answer, payload, previous, RepresentationQualityState.Final);
        await representations.StageTranscriptAsync(userId, header, payload, previous is null ? null : new(previous, header), ct);
        answer.RecordWrittenVersion(header.Id);
        if (!await answers.SaveAsync(ct)) throw Conflict();
        return await MapAsync(answer, ct);
    }, ct);

    public Task<Result<ThinkingAudioDto>> ConfirmAsync(Guid userId, Guid id, ThinkingAudioConfirmDto dto, CancellationToken ct) => TransactionAsync(async () =>
    {
        var answer = await OwnedAsync(userId, id, ct);
        var requestHash = Representation.ComputeTextContentHash(JsonSerializer.Serialize(new
        {
            Schema = 1, dto.ExpectedRevision, dto.ExpectedDeckRevision, dto.RepresentationId, dto.Status
        }));
        if (answer.ConfirmedMemoryId.HasValue)
        {
            // Older receipts cannot prove which request succeeded. Reload them without replaying a write.
            if (answer.ConfirmationRequestHash != requestHash) throw Conflict();
            return await MapAsync(answer, ct);
        }
        if (answer.Revision != dto.ExpectedRevision || answer.RepresentationId != dto.RepresentationId) throw Conflict();
        var (_, layer) = await QuestionAsync(userId, answer.BoardId!.Value, answer.CardId, answer.LayerId, ct);
        if (Hash(layer) != answer.QuestionHash) throw Conflict();
        var previous = await representations.HeaderAsync(dto.RepresentationId, userId, ct) ?? throw Missing();
        var previousText = await representations.TranscriptAsync(previous.Id, userId, ct) ?? throw Missing();
        var saved = await writtenAnswers.AnswerAsync(userId, answer.BoardId.Value, answer.CardId, answer.LayerId,
            new(dto.ExpectedDeckRevision, previousText.Text, dto.Status), ct);
        if (!saved.IsSuccess) throw new DomainException(saved.ErrorCode, saved.ErrorMessage);
        var payload = new Transcript(userId, CaptureSource.Typed, previousText.Text, boardId: answer.BoardId, createdFromCaptureId: answer.CaptureId);
        var header = Header(answer, payload, previous, RepresentationQualityState.Verified);
        await representations.StageTranscriptAsync(userId, header, payload, new(previous, header), ct);
        answer.Confirm(header.Id, saved.Value.Id, requestHash);
        (await work.Boards.GetByIdAsync(answer.BoardId.Value, ct))!.RecordDependentMutation();
        if (!await answers.SaveAsync(ct)) throw Conflict();
        return await MapAsync(answer, ct);
    }, ct);

    public async Task<Result<ThinkingAudioDownload>> DownloadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        try
        {
            var answer = await OwnedAsync(userId, id, ct);
            return Result.Success(await OpenOriginalAsync(answer, ct));
        }
        catch (DomainException ex) { return Result.Failure<ThinkingAudioDownload>(ex.ErrorCode, ex.Message); }
    }

    private async Task<ThinkingAudioDownload> OpenOriginalAsync(ThinkingAudioAnswer answer, CancellationToken ct)
    {
        var capture = await captures.GetByIdForUserAsync(answer.CaptureId, answer.UserId, ct) ?? throw Missing();
        var asset = capture.SourceAssets.Single(x => x.Id == answer.SourceAssetId);
        var stream = await blobs.OpenReferenceReadAsync(asset.BlobReferenceId!.Value, answer.UserId, ct) ?? throw Missing();
        return new ThinkingAudioDownload(stream, asset.MediaType, asset.OriginalName ?? "original-audio");
    }

    private async Task<ThinkingAudioAnswer> OwnedAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var answer = await answers.GetAsync(userId, id, ct) ?? throw Missing();
        if (answer.BoardId is not { } boardId) throw Missing();
        await ReadBoardAsync(userId, boardId, ct); return answer;
    }
    private async Task ReadBoardAsync(Guid userId, Guid boardId, CancellationToken ct)
    {
        var allowed = await authorization.CanReadBoardAsync(userId, boardId);
        if (!allowed.IsSuccess || !allowed.Value) throw Missing();
        var board = await work.Boards.GetByIdAsync(boardId, ct);
        if (board is null || board.IsArchived) throw Missing();
    }
    private async Task<(ThinkingDeck, ThinkingLayer)> QuestionAsync(Guid userId, Guid boardId, Guid cardId, Guid layerId, CancellationToken ct)
    {
        await ReadBoardAsync(userId, boardId, ct);
        if ((await work.Cards.GetByIdAsync(cardId, ct))?.BoardId != boardId) throw Missing();
        var deck = await decks.GetAsync(cardId, ct);
        var layer = deck?.ReadLayers().SingleOrDefault(x => x.Id == layerId && x.Kind == "question");
        return deck is null || layer is null ? throw Missing() : (deck, layer);
    }
    private async Task<ThinkingAudioDto> MapAsync(ThinkingAudioAnswer answer, CancellationToken ct)
    {
        var capture = await captures.GetByIdForUserAsync(answer.CaptureId, answer.UserId, ct) ?? throw Missing();
        var source = capture.SourceAssets.Single(x => x.Id == answer.SourceAssetId);
        var versions = new List<ThinkingAudioRepresentationDto>();
        foreach (var header in (await representations.ListByCaptureAsync(capture.Id, answer.UserId, ct)).OrderBy(x => x.CreatedAt))
        {
            var payload = await representations.TranscriptAsync(header.Id, answer.UserId, ct) ?? throw Missing();
            versions.Add(new(header.Id, payload.Text, header.QualityState.ToString(), header.SupersededByRepresentationId));
        }
        return new(answer.Id, answer.Revision, capture.Id, source.Id, answer.QuestionHash, source.OriginalName ?? "original-audio",
            source.MediaType, source.ByteSize, source.ContentHash, capture.SourceAssets.Single(x => x.Ordinal == 1).TextPayload!.Text,
            answer.RepresentationId, answer.ConfirmedMemoryId, versions);
    }
    private async Task<Result<ThinkingAudioDto>> TransactionAsync(Func<Task<ThinkingAudioDto>> action, CancellationToken ct)
    {
        await work.BeginTransactionAsync(ct); var committed = false;
        try { var result = await action(); await work.CommitTransactionAsync(ct); committed = true; return Result.Success(result); }
        catch (DomainException ex) { return Result.Failure<ThinkingAudioDto>(ex.ErrorCode, ex.Message); }
        finally { if (!committed) await work.RollbackTransactionAsync(CancellationToken.None); }
    }
    private static Representation Header(ThinkingAudioAnswer answer, Transcript payload, Representation? parent, RepresentationQualityState quality) =>
        new(payload.Id, answer.CaptureId, answer.UserId, RepresentationKind.Transcript, parent is null ? answer.SourceAssetId : null,
            parent?.Id, null, quality == RepresentationQualityState.Verified ? "human-confirmation" : "human-written", "1", null,
            Representation.ComputeTextContentHash("thinking-audio-manual-v1"), 1, Representation.ComputeTextContentHash(payload.Text), null,
            quality, ["Written by the owner; no automated transcription or external verification."], DateTimeOffset.UtcNow);
    private static string Hash(ThinkingLayer layer) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { layer.Title, layer.Body }))));
    private static async Task<string> HashUploadAsync(Stream content, long size, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; long total = 0;
        int count;
        while ((count = await content.ReadAsync(buffer, ct)) > 0)
        {
            total += count; if (total > size) throw Invalid("The recording size changed. Choose the file again.");
            hash.AppendData(buffer, 0, count);
        }
        if (total != size) throw Invalid("The recording was incomplete. Keep the original and retry.");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static bool LooksLikeAudio(byte[] bytes, int length, string type) => length >= 12 && type switch
    {
        "audio/webm" => bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }),
        "audio/ogg" => Encoding.ASCII.GetString(bytes, 0, 4) == "OggS",
        "audio/wav" or "audio/x-wav" => Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE",
        "audio/mp4" => Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp",
        "audio/mpeg" => Encoding.ASCII.GetString(bytes, 0, 3) == "ID3" || (bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0),
        _ => false
    };
    private static DomainException Missing() => new(ErrorCodes.NotFound, "This private recording or saved question is unavailable.");
    private static DomainException Conflict() => new(ErrorCodes.Conflict, "The question or recording changed. Keep your draft and reload. Correct confirmed answers in private memory.");
    private static DomainException Invalid(string message) => new(ErrorCodes.ValidationError, message);
}
