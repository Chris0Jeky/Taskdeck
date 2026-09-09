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
}
