using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;
using Capture = Taskdeck.Domain.Entities.Capture;

namespace Taskdeck.Api.Tests;

/// <summary>
/// CF-01 (#2255) acceptance: over a seeded LEGACY queue - rows written straight to the database, as
/// an install that predates the Context Fabric has - the ID-preserving backfill makes every capture
/// readable through <see cref="ICaptureStore"/> under the same id, and the Inbox keeps showing
/// byte-identical items before and after.
/// <para>
/// The rows are seeded through <see cref="TaskdeckDbContext"/> rather than the capture API precisely
/// because the API now admits captures through <c>CaptureIntakeService</c>: a legacy row is one that
/// never went through it, which is exactly what the backfill has to repair.
/// </para>
/// </summary>
public class ContextFabricCaptureBackfillTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ContextFabricCaptureBackfillTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> SeedLegacyCaptureAsync(
        Guid userId,
        string text,
        CaptureSource source = CaptureSource.Typed,
        RequestStatus status = RequestStatus.Pending,
        CaptureDispositionV1? disposition = null,
        CaptureProvenanceV1? provenance = null,
        string? externalRef = null,
        string? titleHint = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            source,
            text,
            TitleHint: titleHint,
            ExternalRef: externalRef,
            Provenance: provenance,
            Disposition: disposition);
        var request = new LlmRequest(
            userId,
            CaptureRequestContract.ResolveRequestTypeForSource(source),
            CaptureRequestContract.SerializePayload(payload),
            boardId: null);

        switch (status)
        {
            case RequestStatus.Processing:
                request.MarkAsProcessing();
                break;
            case RequestStatus.Completed:
                request.MarkAsProcessing();
                request.MarkAsCompleted();
                break;
            case RequestStatus.Failed:
                request.MarkAsProcessing();
                request.MarkAsFailed("triage failed");
                break;
            case RequestStatus.Cancelled:
                request.Cancel();
                break;
        }

        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    /// <summary>
    /// A legacy row whose stored payload is not capture JSON. It reads back through the raw-text
    /// fallback, which is how a row can be genuinely unmappable.
    /// </summary>
    private async Task<Guid> SeedRawCaptureAsync(Guid userId, string payload)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var request = new LlmRequest(userId, CaptureRequestContract.RequestTypeV1, payload);
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<CaptureBackfillResult> RunBackfillAsync(int batchSize = 2)
    {
        using var scope = _factory.Services.CreateScope();
        var backfill = scope.ServiceProvider.GetRequiredService<CaptureBackfillService>();
        return await backfill.RunAsync(batchSize);
    }

    private async Task<List<CaptureItemSummaryDto>> ListInboxAsync()
    {
        var response = await _client.GetAsync("/api/capture/items");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<CaptureItemSummaryDto>>())!;
    }

    [Fact]
    public async Task Backfill_ShouldMakeASeededLegacyQueueReadableThroughTheStoreWithoutChangingTheInbox()
    {
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-golden");

        var proposalId = Guid.NewGuid();
        var pending = await SeedLegacyCaptureAsync(user.UserId, "book the venue", titleHint: "Venue");
        var triaged = await SeedLegacyCaptureAsync(
            user.UserId, "call Dana", CaptureSource.Paste, RequestStatus.Completed);
        var proposed = await SeedLegacyCaptureAsync(
            user.UserId, "ship the release", CaptureSource.TranscriptPaste, RequestStatus.Completed,
            provenance: new CaptureProvenanceV1(Guid.NewGuid(), ProposalId: proposalId));
        var failed = await SeedLegacyCaptureAsync(
            user.UserId, "unreadable audio", CaptureSource.Voice, RequestStatus.Failed);
        var archived = await SeedLegacyCaptureAsync(
            user.UserId, "old note", CaptureSource.Typed, RequestStatus.Cancelled,
            disposition: new CaptureDispositionV1(CaptureDisposition.Archived, DateTimeOffset.UtcNow, user.UserId));
        var kept = await SeedLegacyCaptureAsync(
            user.UserId, "keep this", CaptureSource.Typed, RequestStatus.Pending,
            disposition: new CaptureDispositionV1(CaptureDisposition.Kept, DateTimeOffset.UtcNow, user.UserId));
        var clipped = await SeedLegacyCaptureAsync(
            user.UserId, "the clipped article", CaptureSource.WebClip,
            externalRef: "https://example.test/article");

        var seeded = new[] { pending, triaged, proposed, failed, archived, kept, clipped };

        // The Inbox as it reads BEFORE the backfill: these rows have no durable capture, so every
        // one of them falls back to its queue payload.
        var before = await ListInboxAsync();
        before.Select(item => item.Id).Should().Contain(seeded);

        var result = await RunBackfillAsync();
        result.Migrated.Should().BeGreaterThanOrEqualTo(seeded.Length);
        result.Skipped.Should().Be(0, "every seeded row is mappable");
        result.Remaining.Should().Be(0);
        result.Complete.Should().BeTrue();

        var after = await ListInboxAsync();
        after.Should().BeEquivalentTo(before, "the Inbox is byte-identical across the read switch");

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICaptureStore>();
        var captures = new Dictionary<Guid, Capture>();
        foreach (var id in seeded)
        {
            var capture = await store.GetByIdForUserAsync(id, user.UserId);
            capture.Should().NotBeNull($"capture {id} must be readable under its own id");
            captures[id] = capture!;
            capture!.LegacyRequestId.Should().Be(id, "the backfill is ID-preserving");
            capture.UserId.Should().Be(user.UserId);
        }

        // Every axis is derived from what the row recorded; nothing defaults to Received.
        captures[pending].Timeline.Should().Be(CaptureTimelineStep.Received);
        captures[pending].UserTitle.Should().Be("Venue");
        captures[triaged].ProcessingSummary.Should().Be(CaptureProcessingSummary.Ready);
        captures[triaged].Timeline.Should().Be(CaptureTimelineStep.Understood);
        captures[proposed].ActionState.Should().Be(CaptureActionState.NeedsReview);
        captures[proposed].Timeline.Should().Be(CaptureTimelineStep.NeedsReview);
        captures[failed].ProcessingSummary.Should().Be(CaptureProcessingSummary.Failed);
        captures[failed].Timeline.Should().Be(CaptureTimelineStep.Failed);
        captures[archived].Disposition.Should().Be(CaptureUserDisposition.Archived);
        captures[kept].Disposition.Should().Be(CaptureUserDisposition.Kept);
        captures[kept].RequestedIntent.Should().Be(CaptureIntentMode.Remember);

        // The material arrives as immutable source assets, not as job state.
        captures[pending].CurrentText.Should().Be("book the venue");
        captures[pending].SourceAssets.Should().ContainSingle()
            .Which.StorageKind.Should().Be(SourceAssetStorageKind.InlineText);
        captures[clipped].SourceAssets.Should().HaveCount(2);
        captures[clipped].SourceAssets[1].StorageKind.Should().Be(SourceAssetStorageKind.ExternalReference);
        captures[clipped].SourceAssets[1].ExternalReference.Should().Be("https://example.test/article");

        // The source snapshot survives so the read switch can serve it.
        captures[proposed].LegacySourceSnapshot.Should().Be(CaptureSource.TranscriptPaste);
        captures[failed].LegacySourceSnapshot.Should().Be(CaptureSource.Voice);
    }

    [Fact]
    public async Task Backfill_ShouldBeIdempotentAndOwnerScoped()
    {
        var owner = await ApiTestHarness.AuthenticateAsync(_client, "cf01-idempotent");
        var ownedId = await SeedLegacyCaptureAsync(owner.UserId, "owned material");

        await RunBackfillAsync();
        var second = await RunBackfillAsync();

        second.Migrated.Should().Be(0, "a migrated row leaves the backlog forever");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Captures.CountAsync(capture => capture.Id == ownedId))
            .Should().Be(1, "re-running creates nothing twice");
        (await db.SourceAssets.CountAsync(asset => asset.CaptureId == ownedId))
            .Should().Be(1, "and duplicates no assets either");

        var store = scope.ServiceProvider.GetRequiredService<ICaptureStore>();
        (await store.GetByIdForUserAsync(ownedId, Guid.NewGuid()))
            .Should().BeNull("reads through the store stay owner-scoped");
    }

    [Fact]
    public async Task Backfill_ShouldRecordACompletedMarkerThatArmsTheReadSwitch()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "cf01-marker");
        await RunBackfillAsync();

        using var scope = _factory.Services.CreateScope();
        var backfillStore = scope.ServiceProvider.GetRequiredService<ICaptureBackfillStore>();
        var state = await backfillStore.GetStateAsync(CaptureBackfillState.LegacyQueueBackfillKey);

        state.Should().NotBeNull();
        state!.IsComplete.Should().BeTrue();
        state.Key.Should().Be(CaptureBackfillState.LegacyQueueBackfillKey);
        (await backfillStore.CountLegacyCaptureBacklogAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CaptureCreatedThroughTheApi_ShouldAlreadyBeDurableWithItsSourceAsset()
    {
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-dualwrite");

        var response = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "dual written capture", "paste"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<CaptureItemDto>())!;

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICaptureStore>();
        var capture = await store.GetByIdForUserAsync(created.Id, user.UserId);

        capture.Should().NotBeNull("ContextFabric:DualWriteCaptures is on by default from CF-01");
        capture!.Id.Should().Be(created.Id);
        capture.CurrentText.Should().Be("dual written capture");
        capture.CapturedAtServer.Should().Be(created.CreatedAt);
    }

    [Fact]
    public async Task EditingACapture_ShouldSupersedeItsSourceAndServeTheNewTextThroughTheStore()
    {
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-supersede");

        var created = (await (await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "original wording", "typed")))
            .Content.ReadFromJsonAsync<CaptureItemDto>())!;

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/capture/items/{created.Id}/suggestion",
            new UpdateCaptureSuggestionDto("corrected wording"));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reread = await _client.GetFromJsonAsync<CaptureItemDto>($"/api/capture/items/{created.Id}");
        reread!.RawText.Should().Be("corrected wording");

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICaptureStore>();
        var capture = (await store.GetByIdForUserAsync(created.Id, user.UserId))!;

        capture.SourceAssets.Should().HaveCount(2, "the original source is superseded, never rewritten");
        capture.SourceAssets[0].TextPayload!.Text.Should().Be("original wording");
        capture.SourceAssets[0].SupersededByAssetId.Should().Be(capture.SourceAssets[1].Id);
        capture.SourceAssets[1].TextPayload!.Text.Should().Be("corrected wording");
        capture.SourceAssets[1].SupersedesAssetId.Should().Be(capture.SourceAssets[0].Id);
        capture.CurrentText.Should().Be("corrected wording");
    }

    [Fact]
    public async Task EditingWhileDualWriteIsOff_ShouldNotLeaveTheInboxServingPreEditText()
    {
        // Round-1 review, HIGH-1, reproduced end to end: create, backfill, disarm the dual-write,
        // edit, re-arm. An anti-join backfill reported nothing to do and the Inbox went back to
        // showing the pre-edit text; the divergence guard and the reconcile pass must both hold.
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-divergence");
        var settings = _factory.Services.GetRequiredService<ContextFabricSettings>();

        var created = (await (await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "first draft", "typed")))
            .Content.ReadFromJsonAsync<CaptureItemDto>())!;
        await RunBackfillAsync();

        settings.DualWriteCaptures = false;
        try
        {
            var update = await _client.PutAsJsonAsync(
                $"/api/capture/items/{created.Id}/suggestion",
                new UpdateCaptureSuggestionDto("corrected draft"));
            update.StatusCode.Should().Be(HttpStatusCode.OK);

            // Even with the aggregate deliberately left behind, the read path must not serve stale
            // text: the queue row is the newer writer.
            var whileDisarmed = await _client.GetFromJsonAsync<CaptureItemDto>($"/api/capture/items/{created.Id}");
            whileDisarmed!.RawText.Should().Be("corrected draft");
        }
        finally
        {
            settings.DualWriteCaptures = true;
        }

        var reread = await _client.GetFromJsonAsync<CaptureItemDto>($"/api/capture/items/{created.Id}");
        reread!.RawText.Should().Be("corrected draft", "re-arming the flag must never resurrect pre-edit text");

        var listed = await ListInboxAsync();
        listed.Single(item => item.Id == created.Id).TextExcerpt.Should().Be("corrected draft");

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICaptureStore>();
        var capture = (await store.GetByIdForUserAsync(created.Id, user.UserId))!;
        capture.CurrentText.Should().Be("corrected draft",
            "an aggregate that already exists is kept in step even while the dual-write flag is off");
        capture.SourceAssets.Should().HaveCount(2, "the edit supersedes, it never rewrites");
        capture.SourceAssets[0].TextPayload!.Text.Should().Be("first draft");
        capture.SourceAssets[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ADivergedAggregate_ShouldBeRepairedByTheReconcilePassAndNeverServedStale()
    {
        // The other half of HIGH-1: an aggregate that genuinely fell behind its queue row, which is
        // what a swallowed durable write leaves behind. The edit is applied straight to the stored
        // payload so nothing keeps the capture in step, exactly as that failure would.
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-repair");
        var created = (await (await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "first draft", "typed")))
            .Content.ReadFromJsonAsync<CaptureItemDto>())!;
        await RunBackfillAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var request = await db.LlmRequests.SingleAsync(row => row.Id == created.Id);
            var payload = CaptureRequestContract.ParseStoredPayload(request.Payload);
            request.UpdatePayload(CaptureRequestContract.SerializePayload(payload with { Text = "corrected draft" }));
            await db.SaveChangesAsync();
        }

        // Before the repair: the queue row is the newer writer, so the read path must prefer it.
        var whileDiverged = await _client.GetFromJsonAsync<CaptureItemDto>($"/api/capture/items/{created.Id}");
        whileDiverged!.RawText.Should().Be("corrected draft", "stale aggregate text must never be served");

        var result = await RunBackfillAsync();
        result.Reconciled.Should().BeGreaterThan(0, "the diverged capture is picked up by the divergence join");
        result.Remaining.Should().Be(0);

        using var repaired = _factory.Services.CreateScope();
        var store = repaired.ServiceProvider.GetRequiredService<ICaptureStore>();
        var capture = (await store.GetByIdForUserAsync(created.Id, user.UserId))!;
        capture.CurrentText.Should().Be("corrected draft");
        capture.SourceAssets.Should().HaveCount(2, "the repair supersedes, it never rewrites");
        capture.SourceAssets[0].TextPayload!.Text.Should().Be("first draft");

        // And the pass settles: a second run finds nothing left to do.
        var second = await RunBackfillAsync();
        second.Reconciled.Should().Be(0, "the reconciliation stamp takes the capture out of the backlog");
        second.Remaining.Should().Be(0);
    }

    [Theory]
    [InlineData("keep", CaptureUserDisposition.Kept)]
    [InlineData("archive", CaptureUserDisposition.Archived)]
    public async Task Disposition_ShouldPreserveAQueueTextCorrectionThroughResponseReadAndBackfill(
        string action,
        CaptureUserDisposition expectedDisposition)
    {
        // CF-01c (#2347) verifier repro against the real queue CAS and SQLite stores.
        var user = await ApiTestHarness.AuthenticateAsync(_client, $"cf01-disposition-{action}");
        var created = (await (await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "first draft", "typed")))
            .Content.ReadFromJsonAsync<CaptureItemDto>())!;
        await RunBackfillAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var request = await db.LlmRequests.SingleAsync(row => row.Id == created.Id);
            var payload = CaptureRequestContract.ParseStoredPayload(request.Payload);
            request.UpdatePayload(CaptureRequestContract.SerializePayload(payload with { Text = "corrected draft" }));
            await db.SaveChangesAsync();
        }

        var whileDiverged = await _client.GetFromJsonAsync<CaptureItemDto>($"/api/capture/items/{created.Id}");
        whileDiverged!.RawText.Should().Be("corrected draft", "the queue row is the newer writer before disposition");

        var response = await _client.PostAsync($"/api/capture/items/{created.Id}/{action}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var mutated = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
        mutated!.RawText.Should().Be("corrected draft");

        var reread = await _client.GetFromJsonAsync<CaptureItemDto>($"/api/capture/items/{created.Id}");
        reread!.RawText.Should().Be("corrected draft");

        var backfill = await RunBackfillAsync();
        backfill.Remaining.Should().Be(0, "the disposition repair must leave no masked divergence");

        using var inspected = _factory.Services.CreateScope();
        var store = inspected.ServiceProvider.GetRequiredService<ICaptureStore>();
        var capture = (await store.GetByIdForUserAsync(created.Id, user.UserId))!;
        capture.CurrentText.Should().Be("corrected draft");
        capture.Disposition.Should().Be(expectedDisposition);
        capture.SourceAssets.Should().HaveCount(2, "the original source remains as immutable lineage");
        capture.SourceAssets[0].TextPayload!.Text.Should().Be("first draft");
        capture.SourceAssets[0].IsActive.Should().BeFalse();
        capture.SourceAssets[1].SupersedesAssetId.Should().Be(capture.SourceAssets[0].Id);
    }

    [Fact]
    public async Task Backfill_ShouldKeepAnArchivedTextMismatchReadableAndOutstandingWithoutStampingIt()
    {
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-archived-divergence");
        Guid captureId = Guid.Empty;
        try
        {
            var created = (await (await _client.PostAsJsonAsync(
                "/api/capture/items",
                new CreateCaptureItemDto(null, "first draft", "typed")))
                .Content.ReadFromJsonAsync<CaptureItemDto>())!;
            captureId = created.Id;
            await RunBackfillAsync();

            var archive = await _client.PostAsync($"/api/capture/items/{captureId}/archive", null);
            archive.StatusCode.Should().Be(HttpStatusCode.OK);

            DateTimeOffset archivedStamp;
            using (var before = _factory.Services.CreateScope())
            {
                var store = before.ServiceProvider.GetRequiredService<ICaptureStore>();
                archivedStamp = (await store.GetByIdForUserAsync(captureId, user.UserId))!.UpdatedAt;
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                var request = await db.LlmRequests.SingleAsync(row => row.Id == captureId);
                var payload = CaptureRequestContract.ParseStoredPayload(request.Payload);
                request.UpdatePayload(CaptureRequestContract.SerializePayload(payload with { Text = "corrected draft" }));
                await db.SaveChangesAsync();
            }

            var result = await RunBackfillAsync();

            result.Skipped.Should().BeGreaterThan(0);
            result.Remaining.Should().BeGreaterThan(0);
            result.Complete.Should().BeFalse();
            var reread = await _client.GetFromJsonAsync<CaptureItemDto>($"/api/capture/items/{captureId}");
            reread!.RawText.Should().Be("corrected draft", "the queue fallback owns the unrepaired mismatch");

            using var inspected = _factory.Services.CreateScope();
            var inspectedStore = inspected.ServiceProvider.GetRequiredService<ICaptureStore>();
            var capture = (await inspectedStore.GetByIdForUserAsync(captureId, user.UserId))!;
            capture.CurrentText.Should().Be("first draft");
            capture.UpdatedAt.Should().Be(archivedStamp, "the failed reconciliation earned no stamp");
            capture.SourceAssets.Should().ContainSingle();
        }
        finally
        {
            if (captureId != Guid.Empty)
            {
                // The intentionally outstanding row must not poison this shared fixture's later tests.
                using var cleanup = _factory.Services.CreateScope();
                var db = cleanup.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                await db.Captures.Where(capture => capture.Id == captureId).ExecuteDeleteAsync();
                await db.LlmRequests.Where(row => row.Id == captureId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task Backfill_ShouldStepOverUnmappableRowsAndReportAnHonestMarker()
    {
        // Round-1 review, HIGH-2, reproduced against the real schema: rows whose payload falls back
        // to raw text and exceeds what a source asset may carry sit at the head of the oldest-first
        // backlog. The younger healthy rows behind them must still be brought in, and the marker
        // must not claim completion while anything is outstanding.
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-poisoned");
        var poisoned = new List<Guid>();
        var healthy = new List<Guid>();
        try
        {
            for (var index = 0; index < 3; index++)
            {
                poisoned.Add(await SeedRawCaptureAsync(user.UserId, new string('x', SourceAsset.MaxInlineTextLength + 1)));
            }

            for (var index = 0; index < 5; index++)
            {
                healthy.Add(await SeedLegacyCaptureAsync(user.UserId, $"healthy {index}"));
            }

            var result = await RunBackfillAsync(batchSize: 3);

            result.Skipped.Should().Be(3);
            result.Remaining.Should().Be(3);
            result.Complete.Should().BeFalse("the run must not claim a drained backlog while rows are outstanding");

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            foreach (var id in healthy)
            {
                (await db.Captures.CountAsync(capture => capture.Id == id))
                    .Should().Be(1, "a poisoned head must not stall the rows behind it");
            }

            foreach (var id in poisoned)
            {
                (await db.Captures.CountAsync(capture => capture.Id == id)).Should().Be(0);
            }

            var backfillStore = scope.ServiceProvider.GetRequiredService<ICaptureBackfillStore>();
            var marker = await backfillStore.GetStateAsync(CaptureBackfillState.LegacyQueueBackfillKey);
            marker!.SkippedCount.Should().Be(3, "the marker records what could not be brought in");

            // Nothing disappears: every seeded row is still an Inbox item, poisoned ones included.
            var listed = await ListInboxAsync();
            listed.Select(item => item.Id).Should().Contain(poisoned).And.Contain(healthy);
        }
        finally
        {
            // The unmappable rows would otherwise stay in this shared database and leave every later
            // test in the class with a permanently non-empty backlog.
            using var cleanup = _factory.Services.CreateScope();
            var db = cleanup.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await db.LlmRequests.Where(row => poisoned.Contains(row.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ExportingUserData_ShouldCarryTheDurableCaptureAndItsSources()
    {
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-export");
        var created = (await (await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "exported material", "webClip", ExternalRef: "https://example.test/x")))
            .Content.ReadFromJsonAsync<CaptureItemDto>())!;

        using var scope = _factory.Services.CreateScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IDataExportService>();
        var export = await exporter.ExportUserDataAsync(user.UserId);

        export.IsSuccess.Should().BeTrue(export.ErrorMessage);
        var exported = export.Value!.Data.CaptureItems.Single(item => item.Id == created.Id);
        exported.DurableCapture.Should().NotBeNull("portability must carry the capture own record");
        exported.LegacySource.Should().BeNull("the durable record already carries the material");
        exported.DurableCapture!.Disposition.Should().Be(nameof(CaptureUserDisposition.Active));
        exported.DurableCapture.Timeline.Should().Be(nameof(CaptureTimelineStep.Received));
        exported.DurableCapture.SourceAssets.Should().HaveCount(2);
        exported.DurableCapture.SourceAssets[0].Text.Should().Be("exported material");
        exported.DurableCapture.SourceAssets[1].ExternalReference.Should().Be("https://example.test/x");
    }

    [Fact]
    public async Task ExportingACaptureWithNoDurableRow_ShouldStillCarryItsWords()
    {
        // Round-1 review (Codex 3890182497): portability must not depend on a feature flag. A row the
        // backfill has not reached exports its queue-row material instead of an empty record.
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-export-fallback");
        var legacyId = await SeedLegacyCaptureAsync(
            user.UserId,
            "words that must leave with the user",
            CaptureSource.WebClip,
            externalRef: "https://example.test/fallback",
            titleHint: "Fallback");

        using var scope = _factory.Services.CreateScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IDataExportService>();
        var export = await exporter.ExportUserDataAsync(user.UserId);

        export.IsSuccess.Should().BeTrue(export.ErrorMessage);
        var exported = export.Value!.Data.CaptureItems.Single(item => item.Id == legacyId);
        exported.DurableCapture.Should().BeNull("this row has not been backfilled yet");
        exported.LegacySource.Should().NotBeNull();
        exported.LegacySource!.Text.Should().Be("words that must leave with the user");
        exported.LegacySource.TitleHint.Should().Be("Fallback");
        exported.LegacySource.ExternalRef.Should().Be("https://example.test/fallback");
        exported.LegacySource.Source.Should().Be(nameof(CaptureSource.WebClip));
    }

    [Fact]
    public async Task DeletingTheAccount_ShouldEraseTheDurableCaptureAndItsSources()
    {
        var user = await ApiTestHarness.AuthenticateAsync(_client, "cf01-erasure");
        var created = (await (await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "material to erase", "typed")))
            .Content.ReadFromJsonAsync<CaptureItemDto>())!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Captures.CountAsync(capture => capture.Id == created.Id)).Should().Be(1);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var deletion = scope.ServiceProvider.GetRequiredService<IAccountDeletionService>();
            var result = await deletion.DeleteAccountAsync(
                user.UserId,
                new AccountDeletionRequest("password123", AccountDeletionService.RequiredConfirmationPhrase));
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value!.DurableCapturesDeleted.Should().BeGreaterThan(0);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Captures.CountAsync(capture => capture.UserId == user.UserId)).Should().Be(0);
            (await db.SourceAssets.CountAsync(asset => asset.CaptureId == created.Id)).Should().Be(0);
            (await db.SourceAssetTextPayloads.CountAsync()).Should()
                .Be(await db.SourceAssets.CountAsync(asset => asset.StorageKind == SourceAssetStorageKind.InlineText),
                    "no text payload outlives its asset");
        }
    }
}
