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

            var service = CreateTransactionalService(db, fixture.Item, queueCasResult: true, out var unitOfWork);
            var result = await service.UpdateSuggestionAsync(
                fixture.User.Id,
                fixture.Item.Id,
                new UpdateCaptureSuggestionDto(submittedText));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value.RawText.Should().Be(normalizedText);
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

            var service = CreateTransactionalService(db, fixture.Item, queueCasResult: true, out var unitOfWork);
            var result = await service.UpdateSuggestionAsync(
                fixture.User.Id,
                fixture.Item.Id,
                new UpdateCaptureSuggestionDto(submittedText, TitleHint: titleHint));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
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

    private static (User User, LlmRequest Item, Transcript Original, Taskdeck.Domain.Entities.Capture DurableCapture)
        CreateLinkedTranscriptFixture(string canonicalText)
    {
        var user = new User("source-fidelity", "source-fidelity@example.com", "hash");
        var item = new LlmRequest(
            user.Id,
            CaptureRequestContract.RequestTypeTranscriptV1,
            CaptureRequestContract.SerializePayload(
                new CapturePayloadV1(1, CaptureSource.TranscriptPaste, canonicalText)));
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
            userTitle: null,
            capturedAtServer: item.CreatedAt,
            sourceText: canonicalText);

        return (user, item, original, durableCapture);
    }

    private static CaptureService CreateTransactionalService(
        TaskdeckDbContext db,
        LlmRequest item,
        bool queueCasResult,
        out Mock<IUnitOfWork> unitOfWork)
    {
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
            .ReturnsAsync(queueCasResult);

        unitOfWork = new Mock<IUnitOfWork>();
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
