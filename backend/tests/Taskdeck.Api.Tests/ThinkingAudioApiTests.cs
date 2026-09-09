using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ThinkingAudioApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private async Task<(HttpClient Client, Guid User, Guid Board, Guid Card, ThinkingLayer Question)> Setup()
    {
        var client = factory.CreateClient(); var user = await ApiTestHarness.AuthenticateAsync(client, "audio-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(board.Id, "Thinking", 0); var card = new Card(board.Id, column.Id, "Audio task");
        var question = new ThinkingLayer(Guid.NewGuid(), "question", "What did I learn?", "Original shared context", []);
        var deck = new ThinkingDeck(card.Id); deck.Replace([question]);
        db.Columns.Add(column); db.Cards.Add(card); db.Add(deck); await db.SaveChangesAsync();
        return (client, user.UserId, board.Id, card.Id, question);
    }
    private static string Url(Guid board, Guid card, Guid question) => $"/api/thinking-audio/questions/{board}/{card}/{question}";
    private static byte[] Audio(int size = 70000)
    {
        var bytes = new byte[size]; "RIFF"u8.CopyTo(bytes); "WAVE"u8.CopyTo(bytes.AsSpan(8)); return bytes;
    }
    private static async Task<HttpResponseMessage> Upload(HttpClient client, string url, Guid? uploadId = null,
        byte[]? bytes = null, long revision = 1, long? declaredSize = null, string mime = "audio/wav")
    {
        bytes ??= Audio(); using var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        return await client.PostAsync($"{url}?uploadId={uploadId ?? Guid.NewGuid()}&expectedDeckRevision={revision}&byteSize={declaredSize ?? bytes.Length}&fileName=original.wav", content);
    }
    private static async Task<ThinkingAudioDto> Receipt(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ThinkingAudioDto>())!;
    }

    [Fact]
    public async Task OriginalRemainsUnansweredUntilConfirmedAndRetainsEveryWrittenVersion()
    {
        var (client, user, board, card, question) = await Setup(); var url = Url(board, card, question.Id);
        var uploadId = Guid.NewGuid(); var audio = Audio();
        var original = await Receipt(await Upload(client, url, uploadId, audio));
        original.RepresentationId.Should().BeNull(); original.ConfirmedMemoryId.Should().BeNull(); original.WrittenVersions.Should().BeEmpty();
        original.OriginalEvidence.Should().Contain("Original shared context");
        var memoryUrl = $"/api/boards/{board}/cards/{card}/thinking/questions/{question.Id}/answer";
        (await client.GetAsync(memoryUrl)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var retried = await Receipt(await Upload(client, url, uploadId, audio)); retried.Id.Should().Be(original.Id);
        (await client.GetByteArrayAsync($"/api/thinking-audio/{original.Id}/original")).Should().Equal(audio);
        var written = await Receipt(await client.PutAsJsonAsync($"/api/thinking-audio/{original.Id}/written-version", new ThinkingAudioWriteDto(1, "My first written version")));
        written.WrittenVersions.Single().Quality.Should().Be("Final");
        (await client.GetAsync(memoryUrl)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var corrected = await Receipt(await client.PutAsJsonAsync($"/api/thinking-audio/{original.Id}/written-version", new ThinkingAudioWriteDto(2, "My corrected written version")));
        corrected.WrittenVersions.Should().HaveCount(2).And.Contain(x => x.Text == "My first written version" && x.Quality == "Superseded");
        (await client.PutAsJsonAsync($"/api/thinking-audio/{original.Id}/written-version", new ThinkingAudioWriteDto(1, "Stale draft"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var confirmed = await Receipt(await client.PostAsJsonAsync($"/api/thinking-audio/{original.Id}/confirm", new ThinkingAudioConfirmDto(3, 1, corrected.RepresentationId!.Value, "statement")));
        confirmed.WrittenVersions.Should().HaveCount(3).And.Contain(x => x.Quality == "Verified" && x.Text == "My corrected written version");
        var memory = (await client.GetFromJsonAsync<WorkspaceMemoryDto>(memoryUrl))!;
        memory.Id.Should().Be(confirmed.ConfirmedMemoryId!.Value); memory.Text.Should().Be("My corrected written version");
        var retryConfirm = await Receipt(await client.PostAsJsonAsync($"/api/thinking-audio/{original.Id}/confirm", new ThinkingAudioConfirmDto(3, 1, corrected.RepresentationId.Value, "statement")));
        retryConfirm.Revision.Should().Be(confirmed.Revision); retryConfirm.WrittenVersions.Should().HaveCount(3);
        (await client.GetByteArrayAsync($"/api/thinking-audio/{original.Id}/original")).Should().Equal(audio);
        (await client.PutAsJsonAsync($"/api/thinking-audio/{original.Id}/written-version", new ThinkingAudioWriteDto(4, "Hidden correction"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.StoredBlobs.CountAsync(x => x.OwnerUserId == user)).Should().Be(1);
        (await db.StoredBlobReferences.CountAsync(x => x.OwnerUserId == user)).Should().Be(1);
        (await db.LlmRequests.CountAsync(x => x.UserId == user)).Should().Be(0, "saving audio never schedules a processing job");
        (await db.Cards.FindAsync(card))!.Title.Should().Be("Audio task");
        foreach (var route in new[] { "/api/account/export", "/api/account/export/stream" })
        {
            var export = (await client.GetFromJsonAsync<UserDataExportDto>(route))!;
            var storage = export.Data.SourceStorage!;
            storage.Objects.Should().HaveCount(1); storage.References.Should().HaveCount(1);
            storage.Chunks.OrderBy(x => x.Ordinal).SelectMany(x => x.Content).Should().Equal(audio);
            storage.Representations.Should().HaveCount(3);
            storage.AudioAnswers.Single().ConfirmedMemoryId.Should().Be(memory.Id);
            export.Data.NativeCaptures!.Single(x => x.Id == original.CaptureId).Capture.SourceAssets.Single(x => x.Id == original.SourceAssetId)
                .BlobReferenceId.Should().Be(storage.References.Single().Id);
            export.Data.Transcripts!.Where(x => x.CreatedFromCaptureId == original.CaptureId).Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task PrivacyCoversCollaboratorsUnknownActorsAndRevokedMembership()
    {
        var (owner, _, board, card, question) = await Setup(); var url = Url(board, card, question.Id);
        using var viewer = factory.CreateClient();
        await ApiTestHarness.AssertUnauthorizedAsync(await Upload(viewer, url));
        await ApiTestHarness.AssertUnauthorizedAsync(await viewer.GetAsync(url));
        await ApiTestHarness.AssertUnauthorizedAsync(await viewer.GetAsync($"/api/thinking-audio/{Guid.NewGuid()}/original"));
        await ApiTestHarness.AssertUnauthorizedAsync(await viewer.PutAsJsonAsync($"/api/thinking-audio/{Guid.NewGuid()}/written-version", new ThinkingAudioWriteDto(1, "test")));
        await ApiTestHarness.AssertUnauthorizedAsync(await viewer.PostAsJsonAsync($"/api/thinking-audio/{Guid.NewGuid()}/confirm", new ThinkingAudioConfirmDto(1, 1, Guid.NewGuid(), "statement")));
        var viewerUser = await ApiTestHarness.AuthenticateAsync(viewer, "audio-viewer");
        (await Upload(viewer, url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"/api/boards/{board}/access", new GrantAccessDto(board, viewerUser.UserId, UserRole.Viewer))).EnsureSuccessStatusCode();
        var saved = await Receipt(await Upload(viewer, url));
        (await owner.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/thinking-audio/{saved.Id}/original")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync($"/api/thinking-audio/{saved.Id}/written-version", new ThinkingAudioWriteDto(1, "Foreign write"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"/api/thinking-audio/{saved.Id}/confirm", new ThinkingAudioConfirmDto(1, 1, Guid.NewGuid(), "statement"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetStringAsync($"/api/export/boards/{board}/json")).Should().NotContain(saved.CaptureId.ToString()).And.NotContain(saved.Id.ToString());
        foreach (var route in new[] { "/api/account/export", "/api/account/export/stream" })
            (await owner.GetFromJsonAsync<UserDataExportDto>(route))!.Data.SourceStorage!.Objects.Should().BeEmpty();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await db.BoardAccesses.Where(x => x.UserId == viewerUser.UserId && x.BoardId == board).ExecuteDeleteAsync();
        }
        (await viewer.GetAsync($"/api/thinking-audio/{saved.Id}/original")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await viewer.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidUploadsAndChangedQuestionLeaveNoFalseAnswerOrOrphanBytes()
    {
        var (client, user, board, card, question) = await Setup(); var url = Url(board, card, question.Id);
        (await Upload(client, url, bytes: new byte[15])).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Upload(client, url, declaredSize: 90000)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Upload(client, url, declaredSize: 2 * 1024 * 1024 + 1)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Upload(client, url, revision: 0)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Upload(client, url, mime: "text/html")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.StoredBlobs.CountAsync(x => x.OwnerUserId == user)).Should().Be(0);
            (await db.Captures.CountAsync(x => x.UserId == user)).Should().Be(0);
        }
        var uploadId = Guid.NewGuid(); var original = await Receipt(await Upload(client, url, uploadId));
        var changedBytes = Audio(); changedBytes[20] = 1;
        (await Upload(client, url, uploadId, changedBytes)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Upload(client, url)).StatusCode.Should().Be(HttpStatusCode.Conflict, "an accepted original cannot be silently replaced");
        var written = await Receipt(await client.PutAsJsonAsync($"/api/thinking-audio/{original.Id}/written-version", new ThinkingAudioWriteDto(1, "Unconfirmed text")));
        (await client.PutAsJsonAsync($"/api/boards/{board}/cards/{card}/thinking", new SaveThinkingDeckDto(1, [question with { Body = "Different question" }]))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/thinking-audio/{original.Id}/confirm", new ThinkingAudioConfirmDto(2, 2, written.RepresentationId!.Value, "statement"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetByteArrayAsync($"/api/thinking-audio/{original.Id}/original")).Should().Equal(Audio());
        (await client.GetFromJsonAsync<List<WorkspaceMemoryDto>>($"/api/workspace-memory?boardId={board}"))!.Should().BeEmpty();
    }

    [Fact]
    public async Task BoardRemovalRetainsPortableOriginalsAndAccountErasureRemovesAllOwnedSourceRows()
    {
        var (client, user, board, card, question) = await Setup();
        var original = await Receipt(await Upload(client, Url(board, card, question.Id)));
        var written = await Receipt(await client.PutAsJsonAsync($"/api/thinking-audio/{original.Id}/written-version", new ThinkingAudioWriteDto(1, "Retain my words")));
        await Receipt(await client.PostAsJsonAsync($"/api/thinking-audio/{original.Id}/confirm", new ThinkingAudioConfirmDto(2, 1, written.RepresentationId!.Value, "unknown")));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await db.Boards.Where(x => x.Id == board).ExecuteDeleteAsync();
        }
        (await client.GetAsync($"/api/thinking-audio/{original.Id}/original")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var route in new[] { "/api/account/export", "/api/account/export/stream" })
        {
            var export = (await client.GetFromJsonAsync<UserDataExportDto>(route))!;
            export.Data.SourceStorage!.AudioAnswers.Single().BoardId.Should().BeNull();
            export.Data.SourceStorage.Chunks.OrderBy(x => x.Ordinal).SelectMany(x => x.Content).Should().Equal(Audio());
            export.Data.SourceStorage.Representations.Should().HaveCount(2);
        }
        (await client.PostAsJsonAsync("/api/account/delete", new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"))).EnsureSuccessStatusCode();
        using var finalScope = factory.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await finalDb.StoredBlobs.AnyAsync(x => x.OwnerUserId == user)).Should().BeFalse();
        (await finalDb.StoredBlobReferences.AnyAsync(x => x.OwnerUserId == user)).Should().BeFalse();
        (await finalDb.Captures.AnyAsync(x => x.UserId == user)).Should().BeFalse();
        (await finalDb.Representations.AnyAsync(x => x.UserId == user)).Should().BeFalse();
        (await finalDb.Transcripts.AnyAsync(x => x.UserId == user)).Should().BeFalse();
        (await finalDb.ThinkingAudioAnswers.AnyAsync(x => x.UserId == user)).Should().BeFalse();
    }

    [Fact]
    public async Task BufferedExportRefusesLargeSourceBytesWhileStreamingRetainsOrderedChunks()
    {
        using var client = factory.CreateClient(); var user = await ApiTestHarness.AuthenticateAsync(client, "audio-export-budget");
        var bytes = Audio(7 * 1024 * 1024);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var store = scope.ServiceProvider.GetRequiredService<IBlobStore>();
            await store.AcquireAsync(new(user.UserId, CaptureModality.Audio, bytes.Length, "export-contract", null), new MemoryStream(bytes));
            await tx.CommitAsync();
        }
        (await client.GetAsync("/api/account/export")).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var streamed = (await client.GetFromJsonAsync<UserDataExportDto>("/api/account/export/stream"))!;
        streamed.Data.SourceStorage!.Objects.Single().ByteSize.Should().Be(bytes.Length);
        streamed.Data.SourceStorage.Chunks.Should().OnlyContain(x => x.Content.Length <= StoredBlobChunk.MaximumSize);
        streamed.Data.SourceStorage.Chunks.OrderBy(x => x.Ordinal).SelectMany(x => x.Content).Should().Equal(bytes);
    }
}
