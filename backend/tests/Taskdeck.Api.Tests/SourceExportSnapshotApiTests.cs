using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class SourceExportSnapshotApiTests
{
    [Theory]
    [InlineData("/api/account/export")]
    [InlineData("/api/account/export/stream")]
    public async Task ConcurrentCommittedUploadCannotSplitSourceStorageSections(string route)
    {
        var gate = new ExportGate();
        using var root = new HostedWorkerDisabledTestWebApplicationFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ISourcePortabilityStore>(provider => new GatedStore(
                new SourcePortabilityStore(provider.GetRequiredService<TaskdeckDbContext>()), gate))));
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "snapshot-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        var column = new Column(board.Id, "Next", 0);
        var card = new Card(board.Id, column.Id, "Concurrent original");
        var question = new ThinkingLayer(Guid.NewGuid(), "question", "What changed?", "Original evidence", []);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            db.Columns.Add(column); db.Cards.Add(card);
            var deck = new ThinkingDeck(card.Id); deck.Replace([question]); db.Add(deck);
            await db.SaveChangesAsync();
        }
        ThinkingAudioDto? uploaded = null;
        gate.AfterObjects = async () =>
        {
            // A second HTTP request uses a different scoped connection and commits while the
            // export is between sections. A deferred WAL snapshot must not reserve the writer.
            var bytes = new byte[64]; "RIFF"u8.CopyTo(bytes); "WAVE"u8.CopyTo(bytes.AsSpan(8));
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            using var response = await client.PostAsync($"/api/thinking-audio/questions/{board.Id}/{card.Id}/{question.Id}?uploadId={Guid.NewGuid()}&expectedDeckRevision=1&byteSize=64&fileName=concurrent.wav", content);
            response.EnsureSuccessStatusCode(); uploaded = (await response.Content.ReadFromJsonAsync<ThinkingAudioDto>())!;
            using var written = await client.PutAsJsonAsync($"/api/thinking-audio/{uploaded.Id}/written-version", new ThinkingAudioWriteDto(1, "Committed during export"));
            written.EnsureSuccessStatusCode();
        };
        using var export = await client.GetAsync(route);
        export.EnsureSuccessStatusCode();
        uploaded.Should().NotBeNull("the concurrent writer must finish before later export sections are read");
        using var document = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
        var storage = document.RootElement.GetProperty("data").GetProperty("sourceStorage");
        foreach (var name in new[] { "objects", "references", "chunks", "representations", "audioAnswers" })
            storage.GetProperty(name).GetArrayLength().Should().Be(0, $"{name} must share the pre-upload objects snapshot");

        // A later export sees the whole committed graph, proving the read scope was released.
        using var later = await client.GetAsync(route); later.EnsureSuccessStatusCode();
        using var current = JsonDocument.Parse(await later.Content.ReadAsStringAsync());
        var latest = current.RootElement.GetProperty("data").GetProperty("sourceStorage");
        foreach (var name in new[] { "objects", "references", "chunks", "representations", "audioAnswers" })
            latest.GetProperty(name).GetArrayLength().Should().Be(1);
    }

    private sealed class ExportGate { public Func<Task>? AfterObjects; }
    private sealed class GatedStore(ISourcePortabilityStore inner, ExportGate gate) : ISourcePortabilityStore
    {
        public Task<IAsyncDisposable> OpenReadSnapshotAsync(CancellationToken ct) => inner.OpenReadSnapshotAsync(ct);
        public Task<long> EstimateBufferedBytesAsync(Guid userId, CancellationToken ct) => inner.EstimateBufferedBytesAsync(userId, ct);
        public async IAsyncEnumerable<SourceBlobObjectExportDto> ObjectsAsync(Guid userId, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var row in inner.ObjectsAsync(userId, ct)) yield return row;
            var callback = Interlocked.Exchange(ref gate.AfterObjects, null);
            if (callback is not null) await callback();
        }
        public IAsyncEnumerable<SourceBlobReferenceExportDto> ReferencesAsync(Guid userId, CancellationToken ct) => inner.ReferencesAsync(userId, ct);
        public IAsyncEnumerable<SourceBlobChunkExportDto> ChunksAsync(Guid userId, CancellationToken ct) => inner.ChunksAsync(userId, ct);
        public IAsyncEnumerable<RepresentationDescriptor> RepresentationsAsync(Guid userId, CancellationToken ct) => inner.RepresentationsAsync(userId, ct);
        public IAsyncEnumerable<ThinkingAudioExportDto> AudioAnswersAsync(Guid userId, CancellationToken ct) => inner.AudioAnswersAsync(userId, ct);
        public IAsyncEnumerable<AudioTranscriptionAttemptExportDto> AudioTranscriptionAttemptsAsync(Guid userId, CancellationToken ct) => inner.AudioTranscriptionAttemptsAsync(userId, ct);
        public IAsyncEnumerable<AudioTranscriptionBudgetExportDto> AudioTranscriptionBudgetsAsync(Guid userId, CancellationToken ct) => inner.AudioTranscriptionBudgetsAsync(userId, ct);
    }
}
