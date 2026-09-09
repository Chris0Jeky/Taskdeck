using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class CaptureServiceTransactionIntegrationTests
{
    [Theory]
    [InlineData(CaptureDisposition.Kept, "\r\n")]
    [InlineData(CaptureDisposition.Archived, "\r\n")]
    [InlineData(CaptureDisposition.Kept, "\r")]
    [InlineData(CaptureDisposition.Archived, "\r")]
    public async Task LinkedCorrection_AfterRetryFailure_PreservesRawSourceOnDisposition(
        CaptureDisposition disposition, string lineEnding)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-source-disposition-{Guid.NewGuid():N}.db");
        var rawText = $"corrected{lineEnding}second line";
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath)).Options;
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var fixture = CreateLinkedTranscriptFixture("original transcript");
            db.Users.Add(fixture.User);
            db.LlmRequests.Add(fixture.Item);
            db.Transcripts.Add(fixture.Original);
            db.Captures.Add(fixture.DurableCapture);
            await db.SaveChangesAsync();
            var service = CreateTransactionalService(db, fixture.Item, true, out _, out _, useRealQueue: true);
            var correction = await service.UpdateSuggestionAsync(
                fixture.User.Id, fixture.Item.Id, new UpdateCaptureSuggestionDto(rawText));
            correction.IsSuccess.Should().BeTrue(correction.ErrorMessage);

            db.ChangeTracker.Clear();
            var afterCorrection = await new EfCaptureStore(db).GetByIdForUserAsync(fixture.Item.Id, fixture.User.Id);
            afterCorrection!.CurrentText.Should().Be(rawText);
            var rawAsset = afterCorrection.SourceAssets.Single(value => value.IsActive);
            var row = await db.LlmRequests.SingleAsync(value => value.Id == fixture.Item.Id);
            CaptureRequestContract.ParseStoredPayload(row.Payload).Text.Should().Be("corrected\nsecond line");
            // Keep/Archive is legal after failed re-triage, not immediately after a Triaged correction.
            row.RequeueCompletedCaptureForTriage();
            row.MarkAsFailed("synthetic retry failure");
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = disposition == CaptureDisposition.Kept
                ? await service.KeepAsync(fixture.User.Id, fixture.Item.Id)
                : await service.ArchiveAsync(fixture.User.Id, fixture.Item.Id);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            db.ChangeTracker.Clear();
            var persisted = await new EfCaptureStore(db).GetByIdForUserAsync(fixture.Item.Id, fixture.User.Id);
            persisted!.CurrentText.Should().Be(rawText);
            persisted.SourceAssets.Should().HaveCount(2);
            var active = persisted.SourceAssets.Single(value => value.IsActive);
            active.Id.Should().Be(rawAsset.Id);
            active.ContentHash.Should().Be(rawAsset.ContentHash);
            active.TextPayload!.Text.Should().Be(rawText);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(dbPath + suffix); }
                catch (IOException) { }
            }
        }
    }

    [Fact]
    public async Task LinkedCorrection_WhenQueueCasLoses_RollsBackTranscriptAppendOnSqlite()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-transcript-correction-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var user = new User("correction-rollback", "correction-rollback@example.com", "hash");
            var item = new LlmRequest(
                user.Id,
                CaptureRequestContract.RequestTypeTranscriptV1,
                CaptureRequestContract.SerializePayload(
                    new CapturePayloadV1(1, CaptureSource.TranscriptPaste, "original")));
            item.MarkAsProcessing();
            item.MarkAsCompleted();
            var original = new Transcript(
                user.Id,
                CaptureSource.TranscriptPaste,
                "original",
                [new TranscriptSegment(0, 0, "Speaker", 10)],
                createdFromCaptureId: item.Id);
            item.AttachTranscript(original.Id);
            var durableCapture = Taskdeck.Domain.Entities.Capture.FromQueueRequest(
                item.Id,
                user.Id,
                CaptureSource.TranscriptPaste,
                contextBoardId: null,
                capturedAtClient: null,
                userTitle: null,
                capturedAtServer: item.CreatedAt,
                sourceText: "original");
            db.Users.Add(user);
            db.LlmRequests.Add(item);
            db.Transcripts.Add(original);
            db.Captures.Add(durableCapture);
            await db.SaveChangesAsync();

            var queue = new Mock<ILlmQueueRepository>();
            queue.Setup(repository => repository.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(item);
            queue.Setup(repository => repository.TryCorrectLinkedTranscriptCaptureAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<RequestStatus>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.SetupGet(value => value.LlmQueue).Returns(queue.Object);
            IDbContextTransaction? transaction = null;
            unitOfWork.Setup(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                });
            unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken));
            unitOfWork.Setup(value => value.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        await transaction.DisposeAsync();
                        transaction = null;
                    }
                });
            unitOfWork.Setup(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        await transaction.DisposeAsync();
                        transaction = null;
                    }
                });

            var service = new CaptureService(
                unitOfWork.Object,
                new Mock<IAuthorizationService>().Object,
                captureStore: new EfCaptureStore(db),
                contextFabricSettings: new ContextFabricSettings { DualWriteCaptures = false },
                backfillStore: null,
                logger: null,
                transcriptRepository: new TranscriptRepository(db));

            var result = await service.UpdateSuggestionAsync(
                user.Id,
                item.Id,
                new UpdateCaptureSuggestionDto("replacement"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(Domain.Exceptions.ErrorCodes.Conflict);
            unitOfWork.Verify(value => value.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
            unitOfWork.Verify(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);

            db.ChangeTracker.Clear();
            var persistedItem = await db.LlmRequests.AsNoTracking().SingleAsync(value => value.Id == item.Id);
            var persistedTranscripts = await db.Transcripts
                .AsNoTracking()
                .Where(value => value.CreatedFromCaptureId == item.Id)
                .ToListAsync();
            persistedTranscripts.Should().ContainSingle();
            persistedTranscripts[0].Text.Should().Be("original");
            persistedTranscripts[0].SegmentsJson.Should().Contain("Speaker");
            persistedItem.TranscriptId.Should().Be(original.Id);
            persistedItem.Payload.Should().NotContain("replacement");

            var persistedAssets = await db.SourceAssets
                .AsNoTracking()
                .Include(value => value.TextPayload)
                .Where(value => value.CaptureId == item.Id)
                .OrderBy(value => value.Ordinal)
                .ToListAsync();
            persistedAssets.Should().ContainSingle();
            persistedAssets[0].IsActive.Should().BeTrue();
            persistedAssets[0].TextPayload!.Text.Should().Be("original");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                }
            }
        }
    }

    [Theory]
    [InlineData("corrected\r\nsecond line", "corrected\nsecond line")]
    [InlineData("corrected\rsecond line", "corrected\nsecond line")]
    public async Task LinkedCorrection_PreservesSubmittedLineEndingsInDurableSource_WhileCanonicalTranscriptNormalizes(
        string submittedText,
        string normalizedText)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-transcript-source-fidelity-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var fixture = CreateLinkedTranscriptFixture("original transcript");
            db.Users.Add(fixture.User);
            db.LlmRequests.Add(fixture.Item);
            db.Transcripts.Add(fixture.Original);
            db.Captures.Add(fixture.DurableCapture);
            await db.SaveChangesAsync();

            var service = CreateTransactionalService(
                db,
                fixture.Item,
                queueCasResult: true,
                out var unitOfWork,
                out var getReplacementPayload);
            var result = await service.UpdateSuggestionAsync(
                fixture.User.Id,
                fixture.Item.Id,
                new UpdateCaptureSuggestionDto(submittedText));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value.RawText.Should().Be(normalizedText);
            var queuedPayload = getReplacementPayload();
            queuedPayload.Should().NotBeNull();
            CaptureRequestContract.ParseStoredPayload(queuedPayload!).Text.Should().Be(normalizedText);
            unitOfWork.Verify(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);

            db.ChangeTracker.Clear();
            var persistedTranscripts = await db.Transcripts
                .AsNoTracking()
                .Where(value => value.CreatedFromCaptureId == fixture.Item.Id)
                .ToListAsync();
            persistedTranscripts.Should().HaveCount(2);
            persistedTranscripts.Single(value => value.Id == fixture.Original.Id).Text
                .Should().Be("original transcript");
            persistedTranscripts.Single(value => value.Id != fixture.Original.Id).Text
                .Should().Be(normalizedText);

            var persistedAssets = await db.SourceAssets
                .AsNoTracking()
                .Include(value => value.TextPayload)
                .Where(value => value.CaptureId == fixture.Item.Id)
                .OrderBy(value => value.Ordinal)
                .ToListAsync();
            persistedAssets.Should().HaveCount(2);
            persistedAssets[0].TextPayload!.Text.Should().Be("original transcript");
            persistedAssets[0].IsActive.Should().BeFalse();
            persistedAssets[1].TextPayload!.Text.Should().Be(submittedText);
            persistedAssets[1].IsActive.Should().BeTrue();
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                }
            }
        }
    }

    [Theory]
    [InlineData("original\ntranscript", "Retitled transcript")]
    [InlineData("original\r\ntranscript", null)]
    public async Task LinkedCorrection_TitleOnlyOrNormalizedEquivalentText_DoesNotAppendSource(
        string submittedText,
        string? titleHint)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-transcript-title-only-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var fixture = CreateLinkedTranscriptFixture("original\ntranscript");
            db.Users.Add(fixture.User);
            db.LlmRequests.Add(fixture.Item);
            db.Transcripts.Add(fixture.Original);
            db.Captures.Add(fixture.DurableCapture);
            await db.SaveChangesAsync();

            var service = CreateTransactionalService(
                db,
                fixture.Item,
                queueCasResult: true,
                out var unitOfWork,
                out var getReplacementPayload);
            var result = await service.UpdateSuggestionAsync(
                fixture.User.Id,
                fixture.Item.Id,
                new UpdateCaptureSuggestionDto(submittedText, TitleHint: titleHint));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var queuedPayload = getReplacementPayload();
            queuedPayload.Should().NotBeNull();
            CaptureRequestContract.ParseStoredPayload(queuedPayload!).Text.Should().Be("original\ntranscript");
            unitOfWork.Verify(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);

            db.ChangeTracker.Clear();
            var persistedCapture = await db.Captures
                .AsNoTracking()
                .SingleAsync(value => value.Id == fixture.Item.Id);
            persistedCapture.UserTitle.Should().Be(titleHint);

            var persistedAssets = await db.SourceAssets
                .AsNoTracking()
                .Include(value => value.TextPayload)
                .Where(value => value.CaptureId == fixture.Item.Id)
                .ToListAsync();
            persistedAssets.Should().ContainSingle();
            persistedAssets[0].TextPayload!.Text.Should().Be("original\ntranscript");
            persistedAssets[0].IsActive.Should().BeTrue();

            (await db.Transcripts.AsNoTracking()
                .CountAsync(value => value.CreatedFromCaptureId == fixture.Item.Id))
                .Should().Be(1);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                }
            }
        }
    }

    [Fact]
    public async Task LinkedCorrection_ShouldRejectRawTranscriptOverCapBeforeTransaction()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-transcript-raw-cap-rejection-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var fixture = CreateLinkedTranscriptFixture("original transcript", "Original title");
            db.Users.Add(fixture.User);
            db.LlmRequests.Add(fixture.Item);
            db.Transcripts.Add(fixture.Original);
            db.Captures.Add(fixture.DurableCapture);
            await db.SaveChangesAsync();

            var oversizedText = string.Concat(Enumerable.Repeat("x\r\n", 66_667));
            oversizedText.Length.Should().Be(CaptureRequestContract.MaxTranscriptTextLength + 1);
            oversizedText.Replace("\r\n", "\n", StringComparison.Ordinal).Length
                .Should().BeLessThan(CaptureRequestContract.MaxTranscriptTextLength);

            var service = CreateTransactionalService(
                db,
                fixture.Item,
                queueCasResult: true,
                out var unitOfWork,
                out var getReplacementPayload);
            var result = await service.UpdateSuggestionAsync(
                fixture.User.Id,
                fixture.Item.Id,
                new UpdateCaptureSuggestionDto(oversizedText, TitleHint: "Changed title"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(Domain.Exceptions.ErrorCodes.ValidationError);
            result.ErrorMessage.Should().Contain(CaptureRequestContract.MaxTranscriptTextLength.ToString());
            getReplacementPayload().Should().BeNull();
            unitOfWork.Verify(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
            unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            unitOfWork.Verify(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);

            db.ChangeTracker.Clear();
            var persistedItem = await db.LlmRequests.AsNoTracking().SingleAsync(value => value.Id == fixture.Item.Id);
            var persistedPayload = CaptureRequestContract.ParseStoredPayload(persistedItem.Payload);
            persistedPayload.Text.Should().Be("original transcript");
            persistedPayload.TitleHint.Should().Be("Original title");

            var persistedTranscript = await db.Transcripts
                .AsNoTracking()
                .SingleAsync(value => value.CreatedFromCaptureId == fixture.Item.Id);
            persistedTranscript.Text.Should().Be("original transcript");

            var persistedCapture = await db.Captures
                .AsNoTracking()
                .SingleAsync(value => value.Id == fixture.Item.Id);
            persistedCapture.UserTitle.Should().Be("Original title");
            var persistedAssets = await db.SourceAssets
                .AsNoTracking()
                .Include(value => value.TextPayload)
                .Where(value => value.CaptureId == fixture.Item.Id)
                .ToListAsync();
            persistedAssets.Should().ContainSingle();
            persistedAssets[0].TextPayload!.Text.Should().Be("original transcript");
            persistedAssets[0].IsActive.Should().BeTrue();
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                }
            }
        }
    }

    [Fact]
    public async Task LinkedCorrection_ShouldAcceptExactRawTranscriptCapAndNormalizeCanonicalData()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-transcript-raw-cap-accepted-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var fixture = CreateLinkedTranscriptFixture("original transcript");
            db.Users.Add(fixture.User);
            db.LlmRequests.Add(fixture.Item);
            db.Transcripts.Add(fixture.Original);
            db.Captures.Add(fixture.DurableCapture);
            await db.SaveChangesAsync();

            var exactCapText = string.Concat(Enumerable.Repeat("x\r\n", 66_666)) + "xx";
            exactCapText.Length.Should().Be(CaptureRequestContract.MaxTranscriptTextLength);
            var normalizedText = exactCapText.Replace("\r\n", "\n", StringComparison.Ordinal);
            normalizedText.Length.Should().BeLessThan(exactCapText.Length);

            var service = CreateTransactionalService(
                db,
                fixture.Item,
                queueCasResult: true,
                out var unitOfWork,
                out var getReplacementPayload);
            var result = await service.UpdateSuggestionAsync(
                fixture.User.Id,
                fixture.Item.Id,
                new UpdateCaptureSuggestionDto(exactCapText));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value.RawText.Should().Be(normalizedText);
            var queuedPayload = getReplacementPayload();
            queuedPayload.Should().NotBeNull();
            CaptureRequestContract.ParseStoredPayload(queuedPayload!).Text.Should().Be(normalizedText);
            unitOfWork.Verify(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);

            db.ChangeTracker.Clear();
            var persistedTranscripts = await db.Transcripts
                .AsNoTracking()
                .Where(value => value.CreatedFromCaptureId == fixture.Item.Id)
                .ToListAsync();
            persistedTranscripts.Should().HaveCount(2);
            persistedTranscripts.Single(value => value.Id != fixture.Original.Id).Text.Should().Be(normalizedText);

            var persistedAssets = await db.SourceAssets
                .AsNoTracking()
                .Include(value => value.TextPayload)
                .Where(value => value.CaptureId == fixture.Item.Id)
                .OrderBy(value => value.Ordinal)
                .ToListAsync();
            persistedAssets.Should().HaveCount(2);
            persistedAssets[0].TextPayload!.Text.Should().Be("original transcript");
            persistedAssets[0].IsActive.Should().BeFalse();
            persistedAssets[1].TextPayload!.Text.Should().Be(exactCapText);
            persistedAssets[1].IsActive.Should().BeTrue();
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                }
            }
        }
    }

    private static (User User, LlmRequest Item, Transcript Original, Taskdeck.Domain.Entities.Capture DurableCapture)
        CreateLinkedTranscriptFixture(string canonicalText, string? userTitle = null)
    {
        var user = new User("source-fidelity", "source-fidelity@example.com", "hash");
        var item = new LlmRequest(
            user.Id,
            CaptureRequestContract.RequestTypeTranscriptV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.TranscriptPaste, canonicalText, TitleHint: userTitle)));
        item.MarkAsProcessing();
        item.MarkAsCompleted();
        var original = new Transcript(
            user.Id,
            CaptureSource.TranscriptPaste,
            canonicalText,
            createdFromCaptureId: item.Id);
        item.AttachTranscript(original.Id);
        var durableCapture = Taskdeck.Domain.Entities.Capture.FromQueueRequest(
            item.Id,
            user.Id,
            CaptureSource.TranscriptPaste,
            contextBoardId: null,
            capturedAtClient: null,
            userTitle: userTitle,
            capturedAtServer: item.CreatedAt,
            sourceText: canonicalText);

        return (user, item, original, durableCapture);
    }

    private static CaptureService CreateTransactionalService(
        TaskdeckDbContext db,
        LlmRequest item,
        bool queueCasResult,
        out Mock<IUnitOfWork> unitOfWork,
        out Func<string?> getReplacementPayload,
        bool useRealQueue = false)
    {
        var queue = new Mock<ILlmQueueRepository>();
        string? replacementPayload = null;
        queue.Setup(repository => repository.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        queue.Setup(repository => repository.TryCorrectLinkedTranscriptCaptureAsync(
                It.IsAny<Guid>(),
                It.IsAny<RequestStatus>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, RequestStatus, DateTimeOffset, Guid, string, Guid, string, CancellationToken>(
                (_, _, _, _, _, _, payload, _) => replacementPayload = payload)
            .ReturnsAsync(queueCasResult);

        unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.LlmQueue).Returns(queue.Object);
        if (useRealQueue)
        {
            unitOfWork.SetupGet(value => value.LlmQueue).Returns(new LlmQueueRepository(db));
            unitOfWork.SetupGet(value => value.AutomationProposals).Returns(new AutomationProposalRepository(db));
        }
        IDbContextTransaction? transaction = null;
        unitOfWork.Setup(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            });
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken));
        unitOfWork.Setup(value => value.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await transaction.DisposeAsync();
                    transaction = null;
                }
            });
        unitOfWork.Setup(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    await transaction.DisposeAsync();
                    transaction = null;
                }
            });

        getReplacementPayload = () => replacementPayload;

        return new CaptureService(
            unitOfWork.Object,
            new Mock<IAuthorizationService>().Object,
            captureStore: new EfCaptureStore(db),
            contextFabricSettings: new ContextFabricSettings { DualWriteCaptures = false },
            backfillStore: null,
            logger: null,
            transcriptRepository: new TranscriptRepository(db));
    }
}
