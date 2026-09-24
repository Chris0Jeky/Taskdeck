using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArtefactsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ArtefactsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Endpoints_ShouldRequireAuthentication()
    {
        using var client = _factory.CreateClient();
        using var upload = CreateUpload(PngBytes(), "evidence.png", "image/png");

        await ApiTestHarness.AssertUnauthorizedAsync(await client.PostAsync("/api/artefacts", upload));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.GetAsync($"/api/artefacts/{Guid.NewGuid()}"));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.GetAsync($"/api/artefacts/{Guid.NewGuid()}/content"));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.DeleteAsync($"/api/artefacts/{Guid.NewGuid()}"));
        using var raw = CreateRawUpload(PngBytes(), "image/png");
        await ApiTestHarness.AssertUnauthorizedAsync(await client.PostAsync("/api/v2/artefacts?fileName=evidence.png", raw));
    }

    [Fact]
    public async Task VersionedRawUpload_ShouldRoundTripWithoutChangingLegacyMultipartClients()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "artefact-v2-roundtrip");
        var bytes = PngBytes(97);
        using var raw = CreateRawUpload(bytes, "image/png");

        using var response = await client.PostAsync("/api/v2/artefacts?fileName=raw.png", raw);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<SourceArtefactDto>())!;
        created.ByteSize.Should().Be(bytes.Length);
        response.Headers.Location?.ToString().Should().Be($"/api/artefacts/{created.Id}");
        (await client.GetByteArrayAsync($"/api/artefacts/{created.Id}/content")).Should().Equal(bytes);

        using var legacy = CreateUpload("notes"u8.ToArray(), "notes.txt", "text/plain");
        using var legacyResponse = await client.PostAsync("/api/artefacts", legacy);
        legacyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task VersionedRawUpload_ShouldRejectInvalidMetadataAndBytesWithoutRows()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-v2-validation");
        using var missingName = CreateRawUpload(PngBytes(), "image/png");
        using var wrongSignature = CreateRawUpload("MZ executable"u8.ToArray(), "image/png");
        using var invalidText = CreateRawUpload(new byte[] { 0xEF, 0xBF, 0xBD, 0x00 }, "text/plain");

        await ApiTestHarness.AssertErrorContractAsync(
            await client.PostAsync("/api/v2/artefacts", missingName),
            HttpStatusCode.BadRequest, "ValidationError");
        await ApiTestHarness.AssertErrorContractAsync(
            await client.PostAsync("/api/v2/artefacts?fileName=bad.png", wrongSignature),
            HttpStatusCode.BadRequest, "ValidationError");
        await ApiTestHarness.AssertErrorContractAsync(
            await client.PostAsync("/api/v2/artefacts?fileName=bad.txt", invalidText),
            HttpStatusCode.BadRequest, "ValidationError");
        await AssertNoArtefactRowsAsync(user.UserId);
    }

    [Fact]
    public async Task VersionedRawUpload_ShouldRejectQuotaBeforeReadingSourceStream()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-v2-quota");
        using var first = CreateRawUpload(PngBytes(800), "image/png");
        using var firstResponse = await client.PostAsync("/api/v2/artefacts?fileName=first.png", first);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtefactService>();
        await using var unreadable = new ThrowOnReadStream();
        var denied = await service.CreateStreamingAsync(user.UserId,
            new CreateStreamingArtefactRequest(unreadable, "second.png", "image/png", 800));

        denied.IsSuccess.Should().BeFalse();
        denied.ErrorCode.Should().Be(ErrorCodes.PayloadTooLarge);
        unreadable.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task VersionedRawUpload_ShouldRollBackShortAndOverlongStreams()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-v2-length");
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtefactService>();
        var bytes = PngBytes(32);

        await using var shortBody = new MemoryStream(bytes, writable: false);
        var shortResult = await service.CreateStreamingAsync(user.UserId,
            new CreateStreamingArtefactRequest(shortBody, "short.png", "image/png", bytes.Length + 1));
        shortResult.IsSuccess.Should().BeFalse();
        shortResult.ErrorCode.Should().Be(ErrorCodes.ValidationError);

        await using var longBody = new MemoryStream(bytes, writable: false);
        var longResult = await service.CreateStreamingAsync(user.UserId,
            new CreateStreamingArtefactRequest(longBody, "long.png", "image/png", bytes.Length - 1));
        longResult.IsSuccess.Should().BeFalse();
        longResult.ErrorCode.Should().Be(ErrorCodes.PayloadTooLarge);

        await AssertNoArtefactRowsAsync(user.UserId);
    }

    [Fact]
    public async Task VersionedRawUpload_ShouldRejectForeignBoardBeforeReadingBody()
    {
        using var owner = _factory.CreateClient();
        using var outsider = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(owner, "artefact-v2-board-owner");
        var board = await ApiTestHarness.CreateBoardAsync(owner, "artefact-v2-board");
        var outsiderUser = await ApiTestHarness.AuthenticateAsync(outsider, "artefact-v2-board-outsider");
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtefactService>();
        await using var unreadable = new ThrowOnReadStream();

        var denied = await service.CreateStreamingAsync(outsiderUser.UserId,
            new CreateStreamingArtefactRequest(unreadable, "foreign.png", "image/png", 8, board.Id));

        denied.IsSuccess.Should().BeFalse();
        denied.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        unreadable.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task VersionedRawUpload_ShouldRejectModalityQuotaBeforeReadingBody()
    {
        using var constrained = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SourceStorage:ModalityQuotaBytes"] = "8"
                })));
        using var client = constrained.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-v2-modality-quota");
        await using var scope = constrained.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtefactService>();
        await using var unreadable = new ThrowOnReadStream();

        var denied = await service.CreateStreamingAsync(user.UserId,
            new CreateStreamingArtefactRequest(unreadable, "quota.png", "image/png", 32));

        denied.IsSuccess.Should().BeFalse();
        denied.ErrorCode.Should().Be(ErrorCodes.PayloadTooLarge);
        unreadable.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task VersionedRawUpload_ShouldNotHoldSqliteWriterWhileClientBodyStalls()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-v2-slow-body");
        await using var serviceScope = _factory.Services.CreateAsyncScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<IArtefactService>();
        await using var content = new GateReadStream(PngBytes());
        var upload = service.CreateStreamingAsync(user.UserId,
            new CreateStreamingArtefactRequest(content, "slow.png", "image/png", content.Length));
        await content.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await using var writerScope = _factory.Services.CreateAsyncScope();
            var db = writerScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.Boards.Add(new Board("Independent write", ownerId: user.UserId));
            await db.SaveChangesAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            content.Release();
        }

        (await upload).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task VersionedRawUpload_ShouldCountInFlightReservationAgainstUserQuota()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-v2-inflight-quota");
        await using var firstScope = _factory.Services.CreateAsyncScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IArtefactService>();
        await using var pendingContent = new GateReadStream(PngBytes(800));
        var pending = firstService.CreateStreamingAsync(user.UserId,
            new CreateStreamingArtefactRequest(pendingContent, "pending.png", "image/png", 800));
        await pendingContent.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await using var secondScope = _factory.Services.CreateAsyncScope();
            var secondService = secondScope.ServiceProvider.GetRequiredService<IArtefactService>();
            await using var unreadable = new ThrowOnReadStream();
            var denied = await secondService.CreateStreamingAsync(user.UserId,
                new CreateStreamingArtefactRequest(unreadable, "second.png", "image/png", 800));
            denied.IsSuccess.Should().BeFalse();
            denied.ErrorCode.Should().Be(ErrorCodes.PayloadTooLarge);
            unreadable.ReadCount.Should().Be(0);
        }
        finally
        {
            pendingContent.Release();
        }

        (await pending).IsSuccess.Should().BeTrue();
        await using var inspectScope = _factory.Services.CreateAsyncScope();
        var db = inspectScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.StoredBlobReservations.CountAsync(reservation => reservation.OwnerUserId == user.UserId))
            .Should().Be(0);
    }

    [Fact]
    public async Task UploadAndRetrieve_ShouldRoundTripVerifiedImageAndAuditLifecycle()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-roundtrip");
        var bytes = PngBytes(32);

        var created = await UploadAsync(client, bytes, "evidence.png", "image/png");

        created.Kind.Should().Be(Domain.Enums.ArtefactKind.Image);
        created.ByteSize.Should().Be(bytes.Length);
        created.CaptureSource.Should().Be(Domain.Enums.CaptureSource.Import);

        var metadataResponse = await client.GetAsync($"/api/artefacts/{created.Id}");
        metadataResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid referenceId;
        using (var storageScope = _factory.Services.CreateScope())
        {
            var storage = storageScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var stored = await storage.SourceArtefacts.AsNoTracking().SingleAsync(a => a.Id == created.Id);
            stored.BlobReferenceId.Should().NotBeNull("new artefacts use owner-scoped blob references");
            referenceId = stored.BlobReferenceId!.Value;
            (await storage.ArtefactBlobs.CountAsync(b => b.SourceArtefactId == created.Id)).Should().Be(0);
        }

        var contentResponse = await client.GetAsync($"/api/artefacts/{created.Id}/content");
        contentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        contentResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        contentResponse.Content.Headers.ContentDisposition?.DispositionType.Should().Be("inline");
        (await contentResponse.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        var deleteResponse = await client.DeleteAsync($"/api/artefacts/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/artefacts/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var auditActions = await db.AuditLogs
            .Where(a => a.EntityType == "SourceArtefact" && a.EntityId == created.Id && a.UserId == user.UserId)
            .Select(a => a.Action)
            .ToListAsync();
        auditActions.Should().Contain(Domain.Enums.AuditAction.Created);
        auditActions.Should().Contain(Domain.Enums.AuditAction.Deleted);

        // Releasing the last reference must remove the stored object and its chunks.
        var remainingBlobs = await db.Set<Domain.Entities.ArtefactBlob>()
            .CountAsync(b => b.SourceArtefactId == created.Id);
        remainingBlobs.Should().Be(0);
        (await db.StoredBlobReferences.CountAsync(r => r.Id == referenceId)).Should().Be(0);
        (await db.StoredBlobs.CountAsync(b => b.OwnerUserId == user.UserId)).Should().Be(0);
    }

    [Fact]
    public async Task DuplicateUploads_ShareOnlyOwnersObject_AndReleaseLastReference()
    {
        using var owner = _factory.CreateClient();
        using var other = _factory.CreateClient();
        var firstUser = await ApiTestHarness.AuthenticateAsync(owner, "artefact-dedupe-owner");
        var otherUser = await ApiTestHarness.AuthenticateAsync(other, "artefact-dedupe-other");
        var bytes = PngBytes(96);
        var first = await UploadAsync(owner, bytes, "first.png", "image/png");
        var second = await UploadAsync(owner, bytes, "second.png", "image/png");
        var foreign = await UploadAsync(other, bytes, "foreign.png", "image/png");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var ownerReferences = await db.StoredBlobReferences.Where(r => r.OwnerUserId == firstUser.UserId).ToListAsync();
            ownerReferences.Should().HaveCount(2);
            ownerReferences.Select(r => r.BlobId).Distinct().Should().ContainSingle();
            (await db.StoredBlobs.CountAsync(b => b.OwnerUserId == firstUser.UserId)).Should().Be(1);
            (await db.StoredBlobs.CountAsync(b => b.OwnerUserId == otherUser.UserId)).Should().Be(1);
            ownerReferences.Select(r => r.BlobId).Should().NotContain(
                await db.StoredBlobReferences.Where(r => r.OwnerUserId == otherUser.UserId).Select(r => r.BlobId).SingleAsync());
        }

        (await owner.DeleteAsync($"/api/artefacts/{first.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetByteArrayAsync($"/api/artefacts/{second.Id}/content")).Should().Equal(bytes);
        (await other.GetByteArrayAsync($"/api/artefacts/{foreign.Id}/content")).Should().Equal(bytes);
        (await owner.DeleteAsync($"/api/artefacts/{second.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var finalScope = _factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await finalDb.StoredBlobs.CountAsync(b => b.OwnerUserId == firstUser.UserId)).Should().Be(0);
        (await finalDb.StoredBlobs.CountAsync(b => b.OwnerUserId == otherUser.UserId)).Should().Be(1);
    }

    [Fact]
    public async Task DownloadText_ShouldForceAttachmentDisposition()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "artefact-attachment");
        var created = await UploadAsync(client, "safe notes"u8.ToArray(), "notes.txt", "text/plain");

        var response = await client.GetAsync($"/api/artefacts/{created.Id}/content");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition?.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition?.FileNameStar.Should().Be("notes.txt");
    }

    [Fact]
    public async Task Upload_ShouldRejectMagicByteMismatchAndStreamingOverflow()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "artefact-validation");
        using var renamedExe = CreateUpload("MZ executable"u8.ToArray(), "payload.png", "image/png");
        using var oversized = CreateUpload(new byte[1025], "large.txt", "text/plain");

        var magicResponse = await client.PostAsync("/api/artefacts", renamedExe);
        var sizeResponse = await client.PostAsync("/api/artefacts", oversized);

        await ApiTestHarness.AssertErrorContractAsync(magicResponse, HttpStatusCode.BadRequest, "ValidationError");
        await ApiTestHarness.AssertErrorContractAsync(sizeResponse, HttpStatusCode.RequestEntityTooLarge, "PayloadTooLarge");
    }

    [Fact]
    public async Task ConcurrentUploads_ShouldEnforcePerUserQuotaAtomically()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "artefact-quota");
        var bytes = PngBytes(800);

        var first = PostUploadAsync(client, bytes, "first.png", "image/png");
        var second = PostUploadAsync(client, bytes, "second.png", "image/png");
        var responses = await Task.WhenAll(first, second);

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.RequestEntityTooLarge).Should().Be(1);
    }

    [Fact]
    public async Task BlobStoreQuotaRejection_ReturnsPayloadTooLargeWithoutPersistingRows()
    {
        using var constrained = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SourceStorage:ModalityQuotaBytes"] = "8"
                })));
        using var client = constrained.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-blob-quota");
        using var response = await PostUploadAsync(client, PngBytes(32), "quota.png", "image/png");
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.RequestEntityTooLarge, "PayloadTooLarge");

        using var scope = constrained.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.SourceArtefacts.CountAsync(a => a.UserId == user.UserId)).Should().Be(0);
        (await db.StoredBlobs.CountAsync(b => b.OwnerUserId == user.UserId)).Should().Be(0);
        (await db.StoredBlobReferences.CountAsync(r => r.OwnerUserId == user.UserId)).Should().Be(0);
    }

    [Fact]
    public async Task RetrievalAndDelete_ShouldNotRevealAnotherUsersArtefact()
    {
        using var owner = _factory.CreateClient();
        using var outsider = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(owner, "artefact-owner");
        await ApiTestHarness.AuthenticateAsync(outsider, "artefact-outsider");
        var created = await UploadAsync(owner, PngBytes(), "private.png", "image/png");

        (await outsider.GetAsync($"/api/artefacts/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/artefacts/{created.Id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"/api/artefacts/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BoardScopedUpload_ShouldRequireEditorAccessFromClaims()
    {
        using var owner = _factory.CreateClient();
        using var outsider = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(owner, "artefact-board-owner");
        await ApiTestHarness.AuthenticateAsync(outsider, "artefact-board-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(owner, "artefact-board");
        using var upload = CreateUpload(PngBytes(), "evidence.png", "image/png", board.Id);

        var response = await outsider.PostAsync("/api/artefacts", upload);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Forbidden, "Forbidden");
    }

    [Fact]
    public async Task BoardScopedAudit_ShouldIncludeArtefactCreateAndDeleteAfterArtefactIsRemoved()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "artefact-board-audit");
        var board = await ApiTestHarness.CreateBoardAsync(client, "artefact-audit-board");
        using var upload = CreateUpload(PngBytes(), "audit.png", "image/png", board.Id);
        using var uploadResponse = await client.PostAsync("/api/artefacts", upload);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await uploadResponse.Content.ReadFromJsonAsync<SourceArtefactDto>();

        using var deleteResponse = await client.DeleteAsync($"/api/artefacts/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var boardLogs = (await repo.GetByBoardAsync(board.Id))
            .Where(log =>
                log.EntityType == "SourceArtefact" &&
                log.EntityId == board.Id &&
                log.Changes != null &&
                log.Changes.Contains(created.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        boardLogs.Should().Contain(log => log.Action == AuditAction.Created);
        boardLogs.Should().Contain(log => log.Action == AuditAction.Deleted);

        var queriedBoardLogs = (await repo.QueryAsync(
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(1),
                boardId: board.Id))
            .Where(log =>
                log.EntityType == "SourceArtefact" &&
                log.EntityId == board.Id &&
                log.Changes != null &&
                log.Changes.Contains(created.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        queriedBoardLogs.Should().Contain(log => log.Action == AuditAction.Created);
        queriedBoardLogs.Should().Contain(log => log.Action == AuditAction.Deleted);
    }

    [Fact]
    public async Task Upload_ShouldRejectNonCaptureAndMismatchedBoardProvenance()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-provenance");
        var captureBoard = await ApiTestHarness.CreateBoardAsync(client, "artefact-capture-board");
        var otherBoard = await ApiTestHarness.CreateBoardAsync(client, "artefact-other-board");
        Guid nonCaptureId;
        Guid captureId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var nonCapture = new LlmRequest(user.UserId, "summarize", "{}", captureBoard.Id);
            var capture = new LlmRequest(
                user.UserId,
                CaptureRequestContract.RequestTypeV1,
                "{}",
                captureBoard.Id);
            db.LlmRequests.AddRange(nonCapture, capture);
            await db.SaveChangesAsync();
            nonCaptureId = nonCapture.Id;
            captureId = capture.Id;
        }

        using var nonCaptureUpload = CreateUpload(
            PngBytes(),
            "non-capture.png",
            "image/png",
            createdFromCaptureId: nonCaptureId);
        using var mismatchedUpload = CreateUpload(
            PngBytes(),
            "mismatch.png",
            "image/png",
            otherBoard.Id,
            captureId);

        var nonCaptureResponse = await client.PostAsync("/api/artefacts", nonCaptureUpload);
        var mismatchResponse = await client.PostAsync("/api/artefacts", mismatchedUpload);

        await ApiTestHarness.AssertErrorContractAsync(nonCaptureResponse, HttpStatusCode.BadRequest, "ValidationError");
        await ApiTestHarness.AssertErrorContractAsync(mismatchResponse, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task Upload_ShouldDeriveBoardScopeFromLinkedCapture()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-capture-board-scope");
        var board = await ApiTestHarness.CreateBoardAsync(client, "artefact-derived-board");
        Guid captureId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var capture = new LlmRequest(
                user.UserId,
                CaptureRequestContract.RequestTypeV1,
                "{}",
                board.Id);
            db.LlmRequests.Add(capture);
            await db.SaveChangesAsync();
            captureId = capture.Id;
        }

        using var upload = CreateUpload(
            PngBytes(),
            "derived.png",
            "image/png",
            createdFromCaptureId: captureId);
        using var response = await client.PostAsync("/api/artefacts", upload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<SourceArtefactDto>();
        created!.BoardId.Should().Be(board.Id);
        created.CreatedFromCaptureId.Should().Be(captureId);
    }

    [Fact]
    public async Task Upload_ShouldRecheckActiveUserInsideCommitTransaction()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-active-race");
        await using var serviceScope = _factory.Services.CreateAsyncScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<IArtefactService>();
        await using var content = new GateReadStream(PngBytes());

        var createTask = service.CreateAsync(
            user.UserId,
            new CreateArtefactRequest(content, "race.png", "image/png"));
        await content.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await using var mutationScope = _factory.Services.CreateAsyncScope();
            var db = mutationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var persistedUser = await db.Users.SingleAsync(candidate => candidate.Id == user.UserId);
            persistedUser.Deactivate();
            await db.SaveChangesAsync();
        }
        finally
        {
            content.Release();
        }

        var result = await createTask;

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        await AssertNoArtefactRowsAsync(user.UserId);
    }

    [Fact]
    public async Task Upload_ShouldRecheckBoardEditorAccessInsideCommitTransaction()
    {
        using var ownerClient = _factory.CreateClient();
        using var editorClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "artefact-access-owner");
        var editor = await ApiTestHarness.AuthenticateAsync(editorClient, "artefact-access-editor");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "artefact-access-race");

        await using (var setupScope = _factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.Add(new BoardAccess(board.Id, editor.UserId, UserRole.Editor, owner.UserId));
            await db.SaveChangesAsync();
        }

        await using var serviceScope = _factory.Services.CreateAsyncScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<IArtefactService>();
        await using var content = new GateReadStream(PngBytes());
        var createTask = service.CreateAsync(
            editor.UserId,
            new CreateArtefactRequest(content, "race.png", "image/png", board.Id));
        await content.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await using var mutationScope = _factory.Services.CreateAsyncScope();
            var db = mutationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await db.BoardAccesses
                .Where(access => access.BoardId == board.Id && access.UserId == editor.UserId)
                .ExecuteDeleteAsync();
        }
        finally
        {
            content.Release();
        }

        var result = await createTask;

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await AssertNoArtefactRowsAsync(editor.UserId);
    }

    [Fact]
    public async Task Download_ShouldClearBlobHeadersWhenContentDisappearsAfterMetadataRead()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "artefact-missing-blob");
        var created = await UploadAsync(client, PngBytes(), "missing.png", "image/png");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var referenceId = await db.SourceArtefacts.Where(a => a.Id == created.Id)
                .Select(a => a.BlobReferenceId).SingleAsync();
            await db.StoredBlobReferences
                .Where(reference => reference.Id == referenceId)
                .ExecuteDeleteAsync();
        }

        using var response = await client.GetAsync($"/api/artefacts/{created.Id}/content");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
        response.Content.Headers.ContentDisposition.Should().BeNull();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Artefacts_ShouldExposeNoUpdateEndpoint()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "artefact-immutable");
        var created = await UploadAsync(client, PngBytes(), "immutable.png", "image/png");

        var response = await client.PutAsJsonAsync(
            $"/api/artefacts/{created.Id}",
            new { fileName = "changed.png" });

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task GdprExportAndDeletion_ShouldRoundTripContentAndRemoveBlob()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "artefact-gdpr");
        var bytes = "portable notes"u8.ToArray();
        var created = await UploadAsync(client, bytes, "portable.txt", "text/plain");
        Guid extractionId;
        using (var extractionScope = _factory.Services.CreateScope())
        {
            var extractionService = extractionScope.ServiceProvider
                .GetRequiredService<IArtefactExtractionService>();
            var extractionResult = await extractionService.ExtractAsync(user.UserId, created.Id);
            extractionResult.IsSuccess.Should().BeTrue(extractionResult.ErrorMessage);
            extractionId = extractionResult.Value.Id;
        }

        var exportResponse = await client.GetAsync("/api/account/export");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var export = JsonDocument.Parse(await exportResponse.Content.ReadAsStringAsync());
        var exportedArtefact = export.RootElement.GetProperty("data").GetProperty("artefacts")
            .EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == created.Id);
        Convert.FromBase64String(exportedArtefact.GetProperty("contentBase64").GetString()!).Should().Equal(bytes);
        var exportedExtraction = exportedArtefact.GetProperty("extractions")
            .EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == extractionId);
        exportedExtraction.GetProperty("extractedText").GetString().Should().Be("portable notes");
        exportedExtraction.GetProperty("warnings").GetArrayLength().Should().Be(0);

        var streamExportResponse = await client.GetAsync("/api/account/export/stream");
        streamExportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var streamExport = JsonDocument.Parse(await streamExportResponse.Content.ReadAsStringAsync());
        var streamedArtefact = streamExport.RootElement.GetProperty("data").GetProperty("artefacts")
            .EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == created.Id);
        Convert.FromBase64String(streamedArtefact.GetProperty("contentBase64").GetString()!).Should().Equal(bytes);
        var streamedExtraction = streamedArtefact.GetProperty("extractions")
            .EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == extractionId);
        streamedExtraction.GetProperty("extractedText").GetString().Should().Be("portable notes");

        var deleteResponse = await client.PostAsJsonAsync(
            "/api/account/delete",
            new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var deletionResult = await deleteResponse.Content.ReadFromJsonAsync<AccountDeletionResultDto>();
        deletionResult!.ArtefactsDeleted.Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.SourceArtefacts.CountAsync(a => a.UserId == user.UserId)).Should().Be(0);
        (await db.ArtefactBlobs.CountAsync(b => b.SourceArtefactId == created.Id)).Should().Be(0);
        (await db.StoredBlobs.CountAsync(b => b.OwnerUserId == user.UserId)).Should().Be(0);
        (await db.ArtefactExtractions.CountAsync(e => e.SourceArtefactId == created.Id)).Should().Be(0);
    }

    private static async Task<SourceArtefactDto> UploadAsync(
        HttpClient client,
        byte[] bytes,
        string fileName,
        string mimeType)
    {
        using var response = await PostUploadAsync(client, bytes, fileName, mimeType);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            $"upload response body was: {body}");
        return (await response.Content.ReadFromJsonAsync<SourceArtefactDto>())!;
    }

    private static async Task<HttpResponseMessage> PostUploadAsync(
        HttpClient client,
        byte[] bytes,
        string fileName,
        string mimeType)
    {
        using var content = CreateUpload(bytes, fileName, mimeType);
        return await client.PostAsync("/api/artefacts", content);
    }

    private static MultipartFormDataContent CreateUpload(
        byte[] bytes,
        string fileName,
        string mimeType,
        Guid? boardId = null,
        Guid? createdFromCaptureId = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        form.Add(file, "file", fileName);
        if (boardId.HasValue)
            form.Add(new StringContent(boardId.Value.ToString()), "boardId");
        if (createdFromCaptureId.HasValue)
            form.Add(new StringContent(createdFromCaptureId.Value.ToString()), "createdFromCaptureId");
        return form;
    }

    private async Task AssertNoArtefactRowsAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.SourceArtefacts.CountAsync(artefact => artefact.UserId == userId)).Should().Be(0);
        (await db.StoredBlobReservations.CountAsync(reservation => reservation.OwnerUserId == userId)).Should().Be(0);
        (await db.StoredBlobReferences.CountAsync(reference => reference.OwnerUserId == userId)).Should().Be(0);
        (await db.StoredBlobs.CountAsync(blob => blob.OwnerUserId == userId)).Should().Be(0);
    }

    private sealed class GateReadStream : MemoryStream
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blocked;

        public GateReadStream(byte[] bytes) : base(bytes, writable: false)
        {
        }

        public Task ReadStarted => _readStarted.Task;

        public void Release() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                _readStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private static ByteArrayContent CreateRawUpload(byte[] bytes, string mimeType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        return content;
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public int ReadCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException("Quota rejection must precede stream reads");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            throw new InvalidOperationException("Quota rejection must precede stream reads");
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte[] PngBytes(int length = 8)
    {
        var bytes = new byte[Math.Max(length, 8)];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return bytes;
    }
}
