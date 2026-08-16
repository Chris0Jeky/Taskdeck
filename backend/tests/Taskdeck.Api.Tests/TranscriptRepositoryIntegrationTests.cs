using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class TranscriptRepositoryIntegrationTests
{
    [Fact]
    public async Task GetByUserAsync_IsUserScopedAndPaginatesInDeterministicIdOrder()
    {
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "transcript-owner");
            var other = AddUser(db, "transcript-other");
            var first = AddTranscript(owner.Id, "first");
            var second = AddTranscript(owner.Id, "second");
            var third = AddTranscript(owner.Id, "third");
            db.Transcripts.AddRange(third, first, second, AddTranscript(other.Id, "private"));
            await db.SaveChangesAsync();

            var expectedIds = await db.Transcripts
                .Where(transcript => transcript.UserId == owner.Id)
                .OrderBy(transcript => transcript.Id)
                .Select(transcript => transcript.Id)
                .ToListAsync();

            var repository = new TranscriptRepository(db);
            var firstPage = await repository.GetByUserAsync(owner.Id, limit: 2, offset: 0);
            var secondPage = await repository.GetByUserAsync(owner.Id, limit: 2, offset: 2);
            var foreignLookup = await repository.GetByIdForUserAsync(
                db.Transcripts.Single(transcript => transcript.UserId == other.Id).Id,
                owner.Id);

            firstPage.Select(transcript => transcript.Id).Should().Equal(expectedIds.Take(2));
            secondPage.Select(transcript => transcript.Id).Should().Equal(expectedIds.Skip(2));
            foreignLookup.Should().BeNull("a transcript must never be readable through another user scope");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task DeleteByUserIdAsync_DeletesOnlyThatUsersTranscripts()
    {
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "transcript-delete-owner");
            var other = AddUser(db, "transcript-delete-other");
            db.Transcripts.AddRange(AddTranscript(owner.Id, "delete me"), AddTranscript(other.Id, "retain me"));
            await db.SaveChangesAsync();

            var deleted = await new TranscriptRepository(db).DeleteByUserIdAsync(owner.Id);

            deleted.Should().Be(1);
            (await db.Transcripts.CountAsync(transcript => transcript.UserId == owner.Id)).Should().Be(0);
            (await db.Transcripts.CountAsync(transcript => transcript.UserId == other.Id)).Should().Be(1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task DeleteEvidenceLinksBySourceIdsAsync_BatchesAndDeletesOnlyMatchingTranscriptLinks()
    {
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "transcript-evidence-owner");
            var proposal = new AutomationProposal(
                ProposalSourceType.Queue,
                owner.Id,
                "Transcript evidence proposal",
                RiskLevel.Low,
                Guid.NewGuid().ToString("D"));
            var provenance = new ProposalProvenance(
                proposal.Id,
                proposal.CorrelationId,
                "test-model");
            var field = new ProvenanceField(
                "Operation 1: create card",
                ProvenanceKind.Inferred,
                0.9,
                provenance.Id);
            var targetTranscriptId = Guid.NewGuid();
            var retainedTranscriptId = Guid.NewGuid();
            field.AddEvidenceLink(new ProvenanceEvidenceLink(
                "Transcript",
                targetTranscriptId.ToString("D"),
                field.Id,
                "Transcript evidence",
                2,
                8));
            field.AddEvidenceLink(new ProvenanceEvidenceLink(
                "InboxCapture",
                targetTranscriptId.ToString("D"),
                field.Id,
                "Different source type"));
            field.AddEvidenceLink(new ProvenanceEvidenceLink(
                "Transcript",
                retainedTranscriptId.ToString("D"),
                field.Id,
                "Different transcript"));
            provenance.AddField(field);
            db.AutomationProposals.Add(proposal);
            db.ProposalProvenances.Add(provenance);
            await db.SaveChangesAsync();

            var sourceIds = Enumerable.Range(0, 400)
                .Select(_ => Guid.NewGuid())
                .Append(targetTranscriptId)
                .ToArray();

            var deleted = await new ProposalProvenanceRepository(db)
                .DeleteEvidenceLinksBySourceIdsAsync("Transcript", sourceIds);

            db.ChangeTracker.Clear();
            var remaining = await db.ProvenanceEvidenceLinks
                .OrderBy(link => link.SourceType)
                .ThenBy(link => link.SourceId)
                .ToListAsync();
            deleted.Should().Be(1);
            remaining.Should().HaveCount(2);
            remaining.Should().ContainSingle(link =>
                link.SourceType == "InboxCapture" &&
                link.SourceId == targetTranscriptId.ToString("D"));
            remaining.Should().ContainSingle(link =>
                link.SourceType == "Transcript" &&
                link.SourceId == retainedTranscriptId.ToString("D"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task LlmRequestTranscriptLink_IsUniqueAndSetsNullWhenTranscriptIsDeleted()
    {
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "transcript-link-owner");
            var transcript = AddTranscript(owner.Id, "linked transcript");
            db.Transcripts.Add(transcript);
            var firstRequest = new LlmRequest(owner.Id, "inbox.capture.transcript.v1", "payload");
            firstRequest.AttachTranscript(transcript.Id);
            db.LlmRequests.Add(firstRequest);
            await db.SaveChangesAsync();

            var duplicateRequest = new LlmRequest(owner.Id, "inbox.capture.transcript.v1", "payload-2");
            duplicateRequest.AttachTranscript(transcript.Id);
            db.LlmRequests.Add(duplicateRequest);
            var duplicateSave = () => db.SaveChangesAsync();
            await duplicateSave.Should().ThrowAsync<DbUpdateException>();

            db.ChangeTracker.Clear();
            var persistedRequest = await db.LlmRequests.SingleAsync(request => request.Id == firstRequest.Id);
            var persistedTranscript = await db.Transcripts.SingleAsync(item => item.Id == transcript.Id);
            db.Transcripts.Remove(persistedTranscript);
            await db.SaveChangesAsync();

            await db.Entry(persistedRequest).ReloadAsync();
            persistedRequest.TranscriptId.Should().BeNull();

            var indexes = await db.Database.SqlQueryRaw<string>(
                    "SELECT name AS Value FROM pragma_index_list('LlmRequests')")
                .ToListAsync();
            indexes.Should().Contain("IX_LlmRequests_TranscriptId");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static DbContextOptions<TaskdeckDbContext> CreateOptions(string dbPath) =>
        new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(dbPath))
            .Options;

    private static string CreateDbPath() => Path.Combine(Path.GetTempPath(), $"taskdeck-transcript-{Guid.NewGuid():N}.db");

    private static User AddUser(TaskdeckDbContext db, string username)
    {
        var user = new User(username, $"{username}@example.test", "hash");
        db.Users.Add(user);
        return user;
    }

    private static Transcript AddTranscript(Guid userId, string text)
    {
        var transcript = new Transcript(
            userId,
            CaptureSource.TranscriptPaste,
            text,
            [new TranscriptSegment(0, 0, "Speaker", 0)]);
        return transcript;
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
