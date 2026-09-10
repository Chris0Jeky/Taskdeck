using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AudioTranscriptionApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private sealed class Provider : IAudioTranscriptionProvider
    {
        public SpeechTranscriptionConfiguration Configuration { get; set; } = new(true, "synthetic-speech", "fixture", "https://speech.example",
            new string('a', 64), 60, 5, 10 * 1024 * 1024);
        public int Calls;
        public byte[]? Bytes;
        public Func<Task>? DuringCall;
        public AudioTranscriptionProviderResult Output = new("A provisional spoken answer", null);
        public async Task<AudioTranscriptionProviderResult> TranscribeAsync(byte[] original, string mediaType, CancellationToken ct)
        { Interlocked.Increment(ref Calls); Bytes = original; if (DuringCall is not null) await DuringCall(); return Output; }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private WebApplicationFactory<Program> App(Provider provider, Clock? clock = null) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.RemoveAll<IAudioTranscriptionProvider>(); services.AddScoped<IAudioTranscriptionProvider>(_ => provider);
        if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock); }
    }));
    private static byte[] Bytes() { var bytes = new byte[256]; "RIFF"u8.CopyTo(bytes); "WAVE"u8.CopyTo(bytes.AsSpan(8)); return bytes; }
    private static async Task<(HttpClient Client, Guid User, Guid Board, ThinkingAudioDto Audio)> Setup(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(); var user = await ApiTestHarness.AuthenticateAsync(client, "speech");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(board.Id, "Thinking", 0); var card = new Card(board.Id, column.Id, "Speech task");
        var question = new ThinkingLayer(Guid.NewGuid(), "question", "What did I learn?", "Original context", []);
        var deck = new ThinkingDeck(card.Id); deck.Replace([question]); db.Columns.Add(column); db.Cards.Add(card); db.Add(deck); await db.SaveChangesAsync();
        using var content = new ByteArrayContent(Bytes()); content.Headers.ContentType = new("audio/wav");
        var response = await client.PostAsync($"/api/thinking-audio/questions/{board.Id}/{card.Id}/{question.Id}?uploadId={Guid.NewGuid()}&expectedDeckRevision=1&byteSize=256&fileName=original.wav", content);
        response.EnsureSuccessStatusCode(); return (client, user.UserId, board.Id, (await response.Content.ReadFromJsonAsync<ThinkingAudioDto>())!);
    }
    private static string Url(ThinkingAudioDto audio) => $"/api/thinking-audio/{audio.Id}/transcriptions";
    private static AudioTranscriptionRequestDto Request(ThinkingAudioDto audio, Provider provider) => new(Guid.NewGuid(), audio.Revision, provider.Configuration.ConfigurationHash);
    private static async Task<AudioTranscriptionReceiptDto> Receipt(HttpResponseMessage response)
    { response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<AudioTranscriptionReceiptDto>())!; }

    [Fact]
    public async Task ExplicitRequestCreatesPrivateProvisionalCandidate_ExactRetryDoesNotResend_ExportsIncludeReceipts()
    {
        var provider = new Provider(); using var app = App(provider); var (client, user, _, audio) = await Setup(app);
        provider.Calls.Should().Be(0);
        var request = Request(audio, provider); var receipt = await Receipt(await client.PostAsJsonAsync(Url(audio), request));
        receipt.State.Should().Be("Completed"); receipt.Text.Should().Be(provider.Output.Text); provider.Bytes.Should().Equal(Bytes());
        (await Receipt(await client.PostAsJsonAsync(Url(audio), request))).Should().BeEquivalentTo(receipt); provider.Calls.Should().Be(1);
        (await client.PostAsJsonAsync(Url(audio), request with { ExpectedRevision = 2 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var original = (await client.GetFromJsonAsync<ThinkingAudioLibraryDetail>($"/api/thinking-audio/library/{audio.Id}"))!.Recording;
        original.Revision.Should().Be(1); original.RepresentationId.Should().BeNull(); original.ConfirmedMemoryId.Should().BeNull();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var header = await db.Representations.SingleAsync(x => x.Id == receipt.RepresentationId);
        header.QualityState.Should().Be(RepresentationQualityState.Provisional); header.ParentSourceAssetId.Should().Be(audio.SourceAssetId);
        header.ProcessingRunId.Should().Be(receipt.Id); header.ProcessorModel.Should().Be("fixture");
        (await db.Set<WorkspaceMemory>().CountAsync(x => x.UserId == user)).Should().Be(0);
        foreach (var exportUrl in new[] { "/api/account/export", "/api/account/export/stream" })
        {
            var exported = (await client.GetFromJsonAsync<UserDataExportDto>(exportUrl))!.Data.SourceStorage!;
            exported.AudioTranscriptionAttempts!.Single().Id.Should().Be(receipt.Id);
            exported.AudioTranscriptionBudgets!.Single().Attempts.Should().Be(1);
        }
        var outsider = app.CreateClient(); await ApiTestHarness.AuthenticateAsync(outsider, "speech-other");
        (await outsider.GetAsync(Url(audio))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync(Url(audio), request)).StatusCode.Should().Be(HttpStatusCode.NotFound); provider.Calls.Should().Be(1);
        var adoption = await client.PutAsJsonAsync($"/api/thinking-audio/{audio.Id}/written-version",
            new ThinkingAudioWriteDto(1, "Owner corrected the transcript", receipt.RepresentationId));
        adoption.EnsureSuccessStatusCode(); var written = (await adoption.Content.ReadFromJsonAsync<ThinkingAudioDto>())!;
        var reviewed = await db.Representations.AsNoTracking().SingleAsync(x => x.Id == written.RepresentationId);
        reviewed.ParentRepresentationId.Should().Be(receipt.RepresentationId); reviewed.ProcessorId.Should().Be("human-reviewed-transcript");
        written.ConfirmedMemoryId.Should().BeNull();
        (await client.PutAsJsonAsync($"/api/thinking-audio/{audio.Id}/written-version",
            new ThinkingAudioWriteDto(1, "Owner corrected the transcript", receipt.RepresentationId))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/thinking-audio/{audio.Id}/written-version",
            new ThinkingAudioWriteDto(2, "Forged source", written.RepresentationId))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var confirmation = await client.PostAsJsonAsync($"/api/thinking-audio/{audio.Id}/confirm",
            new ThinkingAudioConfirmDto(written.Revision, 1, written.RepresentationId!.Value, "needsReview"));
        confirmation.EnsureSuccessStatusCode(); var confirmed = (await confirmation.Content.ReadFromJsonAsync<ThinkingAudioDto>())!;
        var finalHeader = await db.Representations.AsNoTracking().SingleAsync(x => x.Id == confirmed.RepresentationId);
        finalHeader.ParentRepresentationId.Should().Be(written.RepresentationId);
        finalHeader.Warnings.Single().Should().Contain("transcription provenance").And.NotContain("no automated transcription");
    }

    [Fact]
    public async Task FailedReceiptRequiresNewExplicitAttempt_AndDailyBudgetCannotBeReplayed()
    {
        var provider = new Provider { Output = new(null, "provider-unavailable") }; provider.Configuration = provider.Configuration with { DailyAttempts = 2 };
        using var app = App(provider); var (client, _, _, audio) = await Setup(app); var request = Request(audio, provider);
        var failed = await Receipt(await client.PostAsJsonAsync(Url(audio), request)); failed.State.Should().Be("Failed");
        (await Receipt(await client.PostAsJsonAsync(Url(audio), request))).Id.Should().Be(failed.Id); provider.Calls.Should().Be(1);
        provider.Output = new("Retry candidate", null);
        (await Receipt(await client.PostAsJsonAsync(Url(audio), Request(audio, provider)))).State.Should().Be("Completed");
        (await client.PostAsJsonAsync(Url(audio), Request(audio, provider))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var status = (await client.GetFromJsonAsync<AudioTranscriptionStatusDto>(Url(audio)))!;
        status.AttemptsUsedToday.Should().Be(2); status.InputBytesUsedToday.Should().Be(512); status.Attempts.Should().HaveCount(2); provider.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentRequestReturnsRunningReceipt_WithoutDoubleDispatchOrAdmission()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { DuringCall = async () => { entered.SetResult(); await release.Task; } };
        using var app = App(provider); var (client, _, _, audio) = await Setup(app); var request = Request(audio, provider);
        var first = client.PostAsJsonAsync(Url(audio), request);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await Receipt(await client.PostAsJsonAsync(Url(audio), request))).State.Should().Be("Running");
            (await client.PostAsJsonAsync(Url(audio), Request(audio, provider))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            provider.Calls.Should().Be(1);
        }
        finally { release.TrySetResult(); }
        (await Receipt(await first)).State.Should().Be("Completed");
    }

    [Fact]
    public async Task LateCompletionExpires_WithoutPublishing_AndNextExplicitAttemptWorks()
    {
        var clock = new Clock(); var provider = new Provider { DuringCall = () => { clock.Now = clock.Now.AddMinutes(3); return Task.CompletedTask; } };
        using var app = App(provider, clock); var (client, user, _, audio) = await Setup(app);
        var receipt = await Receipt(await client.PostAsJsonAsync(Url(audio), Request(audio, provider)));
        receipt.State.Should().Be("Expired"); receipt.RepresentationId.Should().BeNull(); receipt.Text.Should().BeNull();
        using (var scope = app.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Representations.CountAsync(x => x.UserId == user)).Should().Be(0);
        provider.DuringCall = null;
        (await Receipt(await client.PostAsJsonAsync(Url(audio), Request(audio, provider)))).State.Should().Be("Completed");
    }

    [Fact]
    public async Task ErasedOriginalCannotBeResurrected_AndErasureDoesNotResetDailyAdmission()
    {
        var provider = new Provider(); using var app = App(provider); var (client, user, _, audio) = await Setup(app);
        provider.DuringCall = async () =>
        {
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<ICaptureStore>().DeleteByUserAsync(user);
            await tx.CommitAsync();
        };
        (await client.PostAsJsonAsync(Url(audio), Request(audio, provider))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var verify = app.Services.CreateScope(); var data = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await data.AudioTranscriptionAttempts.AnyAsync(x => x.UserId == user)).Should().BeFalse();
        (await data.Representations.AnyAsync(x => x.UserId == user)).Should().BeFalse();
        (await data.AudioTranscriptionBudgets.SingleAsync(x => x.UserId == user)).Attempts.Should().Be(1);
    }

    [Fact]
    public async Task DeactivatedOwnerCannotReceiveProviderResult()
    {
        var provider = new Provider(); using var app = App(provider); var (client, user, _, audio) = await Setup(app);
        provider.DuringCall = async () =>
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Users.Where(x => x.Id == user).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));
        };
        var receipt = await Receipt(await client.PostAsJsonAsync(Url(audio), Request(audio, provider)));
        receipt.FailureCode.Should().Be("access-changed"); receipt.Text.Should().BeNull(); receipt.RepresentationId.Should().BeNull();
    }

    [Fact]
    public async Task RemovedBoardOriginalStillTranscribes_AccountErasureRemovesBudgetAndCandidates()
    {
        var provider = new Provider(); using var app = App(provider); var (client, user, board, audio) = await Setup(app);
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Boards.Where(x => x.Id == board).ExecuteDeleteAsync();
        (await Receipt(await client.PostAsJsonAsync(Url(audio), Request(audio, provider)))).State.Should().Be("Completed");
        using var deletion = new HttpRequestMessage(HttpMethod.Post, "/api/account/delete")
        { Content = JsonContent.Create(new AccountDeletionRequest("password123", AccountDeletionService.RequiredConfirmationPhrase)) };
        var response = await client.SendAsync(deletion); response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var verify = app.Services.CreateScope(); var db = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.AudioTranscriptionBudgets.AnyAsync(x => x.UserId == user)).Should().BeFalse();
        (await db.AudioTranscriptionAttempts.AnyAsync(x => x.UserId == user)).Should().BeFalse();
        (await db.Representations.AnyAsync(x => x.UserId == user)).Should().BeFalse();
    }

    [Fact]
    public async Task PausedOrChangedConfigurationDoesNotDispatchOrSpendBudget()
    {
        var provider = new Provider(); using var app = App(provider); var (client, user, _, audio) = await Setup(app);
        (await client.PostAsJsonAsync(Url(audio), Request(audio, provider) with { ConfigurationHash = new string('b', 64) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = app.Services.CreateScope(); var kill = scope.ServiceProvider.GetRequiredService<ILlmKillSwitchService>();
        await kill.SetKillSwitchAsync(KillSwitchScope.Identity, user.ToString(), true, "synthetic pause");
        (await client.PostAsJsonAsync(Url(audio), Request(audio, provider))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var status = (await client.GetFromJsonAsync<AudioTranscriptionStatusDto>(Url(audio)))!;
        status.Configuration.Enabled.Should().BeFalse(); status.AttemptsUsedToday.Should().Be(0); provider.Calls.Should().Be(0);
    }
}
