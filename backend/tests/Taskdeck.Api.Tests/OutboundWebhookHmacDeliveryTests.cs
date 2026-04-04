using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests that the OutboundWebhookDeliveryWorker computes and sends HMAC-SHA256 signatures
/// that a receiver can independently verify using the subscription signing secret.
/// Covers: correct header format, round-trip verification, timestamp consistency,
/// secret rotation, and edge cases (empty payload, large payload, wrong key rejection).
/// </summary>
public class OutboundWebhookHmacDeliveryTests
{
    // Use a TEST-NET address so host-policy checks pass without DNS.
    private const string AllowedEndpoint = "https://203.0.113.20/webhook";

    // ---------------------------------------------------------------------------
    // Header presence and format
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliveryWorker_ShouldSendSignatureHeader_OnSuccessfulDispatch()
    {
        var signingSecret = "test-signing-secret-abc123";
        var subscription = MakeSubscription(AllowedEndpoint, signingSecret);
        var delivery = MakeDelivery(subscription, "{\"event\":\"card.created\",\"id\":\"1\"}");
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new CapturingHandler(capturedRequests, HttpStatusCode.OK);

        await RunWorkerAsync(delivery, handler);

        capturedRequests.Should().ContainSingle();
        capturedRequests[0].Headers.TryGetValues("X-Taskdeck-Webhook-Signature", out var sigValues)
            .Should().BeTrue("the worker must attach the HMAC signature header");
        var sigHeader = sigValues!.Single();
        sigHeader.Should().StartWith("sha256=", "the signature header must be prefixed with sha256=");
        sigHeader["sha256=".Length..].Should().MatchRegex("^[a-f0-9]{64}$",
            "the signature value must be a 64-character lowercase hex HMAC-SHA256 digest");
    }

    [Fact]
    public async Task DeliveryWorker_ShouldSendTimestampHeader_OnSuccessfulDispatch()
    {
        var subscription = MakeSubscription(AllowedEndpoint, "any-secret");
        var delivery = MakeDelivery(subscription, "{}");
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new CapturingHandler(capturedRequests, HttpStatusCode.OK);
        var beforeDispatch = DateTimeOffset.UtcNow;

        await RunWorkerAsync(delivery, handler);

        capturedRequests.Should().ContainSingle();
        capturedRequests[0].Headers.TryGetValues("X-Taskdeck-Webhook-Timestamp", out var tsValues)
            .Should().BeTrue("the worker must attach the delivery timestamp header");
        var tsHeader = tsValues!.Single();
        tsHeader.Should().MatchRegex(@"^\d+$", "the timestamp header must be a Unix epoch integer");
        var epoch = long.Parse(tsHeader);
        epoch.Should().BeGreaterThanOrEqualTo(beforeDispatch.ToUnixTimeSeconds(),
            "the timestamp must not pre-date the dispatch");
    }

    // ---------------------------------------------------------------------------
    // HMAC round-trip verification
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliveryWorker_ShouldSendSignature_ThatCanBeVerifiedWithSubscriptionSecret()
    {
        const string signingSecret = "round-trip-secret-XYZ";
        const string payload = "{\"event\":\"card.updated\",\"cardId\":\"abc\"}";
        var subscription = MakeSubscription(AllowedEndpoint, signingSecret);
        var delivery = MakeDelivery(subscription, payload);
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new CapturingHandler(capturedRequests, HttpStatusCode.OK);

        await RunWorkerAsync(delivery, handler);

        var request = capturedRequests.Single();
        var receivedTimestamp = long.Parse(
            request.Headers.GetValues("X-Taskdeck-Webhook-Timestamp").Single());
        var receivedSig = request.Headers.GetValues("X-Taskdeck-Webhook-Signature").Single();
        receivedSig.Should().StartWith("sha256=");
        var receivedHex = receivedSig["sha256=".Length..];

        // Independently recompute the expected signature the same way OutboundWebhookSignature does.
        var canonical = $"{receivedTimestamp}.{payload}";
        var secretBytes = Encoding.UTF8.GetBytes(signingSecret);
        var canonicalBytes = Encoding.UTF8.GetBytes(canonical);
        using var hmac = new HMACSHA256(secretBytes);
        var expectedHex = Convert.ToHexString(hmac.ComputeHash(canonicalBytes)).ToLowerInvariant();

        receivedHex.Should().Be(expectedHex,
            "a receiver using the subscription secret must be able to verify the HMAC independently");
    }

    [Fact]
    public async Task DeliveryWorker_ShouldSendCorrectPayloadBody_MatchingSignedContent()
    {
        const string signingSecret = "body-match-secret";
        const string payload = "{\"event\":\"column.created\",\"id\":\"col-1\"}";
        var subscription = MakeSubscription(AllowedEndpoint, signingSecret);
        var delivery = MakeDelivery(subscription, payload);
        string? capturedBody = null;
        string? capturedContentType = null;

        // Read the body and content-type inside the handler before HttpClient disposes them.
        var handler = new CapturingBodyHandler(
            onRequest: async (req) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
                capturedContentType = req.Content.Headers.ContentType?.MediaType;
            },
            statusCode: HttpStatusCode.OK);

        await RunWorkerAsync(delivery, handler);

        capturedBody.Should().Be(payload, "the request body must match the payload that was signed");
        capturedContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task DeliveryWorker_ShouldSendDeliveryIdAndEventTypeHeaders()
    {
        var subscription = MakeSubscription(AllowedEndpoint, "secret");
        var delivery = MakeDelivery(subscription, "{\"data\":1}");
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new CapturingHandler(capturedRequests, HttpStatusCode.OK);

        await RunWorkerAsync(delivery, handler);

        var request = capturedRequests.Single();
        request.Headers.TryGetValues("X-Taskdeck-Webhook-Delivery-Id", out var deliveryIds)
            .Should().BeTrue();
        deliveryIds!.Single().Should().Be(delivery.Id.ToString("D"));

        request.Headers.TryGetValues("X-Taskdeck-Webhook-Event", out var events)
            .Should().BeTrue();
        events!.Single().Should().Be("card.updated");
    }

    // ---------------------------------------------------------------------------
    // Wrong-key rejection (receiver-side mismatch detection)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliveryWorker_SignatureHeader_ShouldNotVerify_WhenWrongKeyUsed()
    {
        const string actualSecret = "correct-secret-abc";
        const string wrongSecret = "wrong-secret-xyz";
        const string payload = "{\"event\":\"card.deleted\"}";
        var subscription = MakeSubscription(AllowedEndpoint, actualSecret);
        var delivery = MakeDelivery(subscription, payload);
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new CapturingHandler(capturedRequests, HttpStatusCode.OK);

        await RunWorkerAsync(delivery, handler);

        var request = capturedRequests.Single();
        var receivedTimestamp = long.Parse(
            request.Headers.GetValues("X-Taskdeck-Webhook-Timestamp").Single());
        var receivedHex = request.Headers.GetValues("X-Taskdeck-Webhook-Signature")
            .Single()["sha256=".Length..];

        // Attempt to verify with the WRONG key — must not match.
        var canonical = $"{receivedTimestamp}.{payload}";
        var wrongKeyBytes = Encoding.UTF8.GetBytes(wrongSecret);
        using var hmac = new HMACSHA256(wrongKeyBytes);
        var wrongHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        wrongHex.Should().NotBe(receivedHex,
            "a receiver using a different key must not be able to forge or verify the signature");
    }

    // ---------------------------------------------------------------------------
    // Secret rotation: new deliveries use the new secret
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliveryWorker_ShouldUseCurrentSigningSecret_AfterRotation()
    {
        const string oldSecret = "old-secret-001";
        const string newSecret = "new-secret-002";
        const string payload = "{\"event\":\"card.moved\"}";

        // Delivery made before rotation uses the old secret.
        var subscription = MakeSubscription(AllowedEndpoint, oldSecret);
        var deliveryBefore = MakeDelivery(subscription, payload);
        var capturedBefore = new List<HttpRequestMessage>();
        await RunWorkerAsync(deliveryBefore, new CapturingHandler(capturedBefore, HttpStatusCode.OK));

        // Rotate the secret on the same subscription record and dispatch a new delivery.
        subscription.RotateSecret(newSecret);
        var deliveryAfter = MakeDelivery(subscription, payload);
        var capturedAfter = new List<HttpRequestMessage>();
        await RunWorkerAsync(deliveryAfter, new CapturingHandler(capturedAfter, HttpStatusCode.OK));

        var sigBefore = capturedBefore.Single().Headers.GetValues("X-Taskdeck-Webhook-Signature").Single();
        var sigAfter = capturedAfter.Single().Headers.GetValues("X-Taskdeck-Webhook-Signature").Single();

        // Even if timestamps happen to be the same epoch-second (rare but possible in fast tests),
        // different keys MUST produce different signatures.
        var tsBefore = long.Parse(capturedBefore.Single().Headers.GetValues("X-Taskdeck-Webhook-Timestamp").Single());
        var tsAfter = long.Parse(capturedAfter.Single().Headers.GetValues("X-Taskdeck-Webhook-Timestamp").Single());

        var canonicalBefore = $"{tsBefore}.{payload}";
        var canonicalAfter = $"{tsAfter}.{payload}";

        var oldHmac = ComputeHmac(oldSecret, canonicalBefore);
        var newHmac = ComputeHmac(newSecret, canonicalAfter);

        // Before-delivery verifies with old secret.
        sigBefore.Should().Be($"sha256={oldHmac}",
            "the delivery before rotation must be signed with the old secret");

        // After-delivery verifies with new secret.
        sigAfter.Should().Be($"sha256={newHmac}",
            "the delivery after rotation must be signed with the new secret");

        // The two signatures must differ (different keys).
        sigBefore.Should().NotBe(sigAfter,
            "rotating the signing secret must change the HMAC output");
    }

    // ---------------------------------------------------------------------------
    // Edge cases: empty payload, large payload
    // ---------------------------------------------------------------------------

    [Fact]
    public void Signature_ShouldProduceVerifiableHmac_ForEmptyPayload()
    {
        // The domain entity requires a non-empty payload, so we test the HMAC primitive
        // directly for the empty-string edge case.  A receiver might encounter an empty
        // body and must still be able to compute and compare the expected signature.
        const string signingSecret = "empty-payload-secret";
        const string payload = "";
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        var computed = OutboundWebhookSignature.Compute(signingSecret, timestamp, payload);

        computed.Should().MatchRegex("^[a-f0-9]{64}$",
            "empty payload must still produce a 64-char hex HMAC digest");

        // Verify the receiver can independently reproduce the same value.
        var expectedHex = ComputeHmac(signingSecret, $"{timestamp.ToUnixTimeSeconds()}.{payload}");
        computed.Should().Be(expectedHex,
            "empty payload HMAC must be reproducible with the same key and timestamp");
    }

    [Fact]
    public async Task DeliveryWorker_ShouldSendVerifiableSignature_ForLargePayload()
    {
        const string signingSecret = "large-payload-secret";
        var payload = new string('x', 100_000);
        var subscription = MakeSubscription(AllowedEndpoint, signingSecret);
        var delivery = MakeDelivery(subscription, payload);
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new CapturingHandler(capturedRequests, HttpStatusCode.OK);

        await RunWorkerAsync(delivery, handler);

        var request = capturedRequests.Single();
        var receivedTimestamp = long.Parse(
            request.Headers.GetValues("X-Taskdeck-Webhook-Timestamp").Single());
        var receivedHex = request.Headers.GetValues("X-Taskdeck-Webhook-Signature")
            .Single()["sha256=".Length..];

        var expectedHex = ComputeHmac(signingSecret, $"{receivedTimestamp}.{payload}");
        receivedHex.Should().Be(expectedHex,
            "large payloads must produce a valid, verifiable HMAC");
    }

    // ---------------------------------------------------------------------------
    // Signature determinism: same inputs must always produce the same digest
    // ---------------------------------------------------------------------------

    [Fact]
    public void Signature_ShouldBeDeterministic_ForSameInputs()
    {
        // OutboundWebhookSignature.Compute must be deterministic: a receiver that
        // recomputes the expected HMAC and compares it to the received header
        // (using CryptographicOperations.FixedTimeEquals) relies on this property.
        var secret = "determinism-secret";
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var payload = "{\"id\":\"t1\"}";

        var sig1 = OutboundWebhookSignature.Compute(secret, timestamp, payload);
        var sig2 = OutboundWebhookSignature.Compute(secret, timestamp, payload);

        sig1.Should().Be(sig2,
            "the same key, timestamp, and payload must always produce an identical digest " +
            "so that receiver-side constant-time comparison can succeed");
    }

    [Fact]
    public void Signature_ShouldDiffer_WhenKeyDiffers()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var payload = "{\"id\":\"t2\"}";

        var correct = OutboundWebhookSignature.Compute("correct-key", timestamp, payload);
        var wrong = OutboundWebhookSignature.Compute("wrong-key", timestamp, payload);

        correct.Should().NotBe(wrong,
            "different signing keys must produce different digests, " +
            "ensuring a receiver using the wrong key correctly fails verification");
    }

    // ---------------------------------------------------------------------------
    // Helper factories and infrastructure
    // ---------------------------------------------------------------------------

    private static string ComputeHmac(string secret, string canonical)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var canonicalBytes = Encoding.UTF8.GetBytes(canonical);
        using var hmac = new HMACSHA256(secretBytes);
        return Convert.ToHexString(hmac.ComputeHash(canonicalBytes)).ToLowerInvariant();
    }

    private static OutboundWebhookSubscription MakeSubscription(string endpointUrl, string signingSecret)
    {
        return new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            endpointUrl,
            signingSecret,
            ["card.*"]);
    }

    private static OutboundWebhookDelivery MakeDelivery(
        OutboundWebhookSubscription subscription,
        string payload)
    {
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            subscription.Id,
            subscription.BoardId,
            "card.updated",
            payload);

        // Inject the subscription via reflection (EF navigation property).
        var prop = typeof(OutboundWebhookDelivery).GetProperty(
            nameof(OutboundWebhookDelivery.Subscription),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        prop.Should().NotBeNull();
        prop!.SetValue(delivery, subscription);

        return delivery;
    }

    private static async Task RunWorkerAsync(
        OutboundWebhookDelivery delivery,
        HttpMessageHandler handler)
    {
        var deliveryRepository = new CapturingDeliveryRepository(delivery);
        var unitOfWork = new StubUnitOfWork(deliveryRepository);
        using var serviceProvider = new ServiceCollection()
            .AddSingleton<IUnitOfWork>(unitOfWork)
            .BuildServiceProvider();
        var httpClientFactory = new FixedClientFactory(new HttpClient(handler));
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings { MaxBatchSize = 5, QueuePollIntervalSeconds = 1, MaxRetries = 3 },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);
    }

    private static async Task InvokeProcessDueDeliveriesAsync(
        OutboundWebhookDeliveryWorker worker,
        CancellationToken cancellationToken)
    {
        var method = typeof(OutboundWebhookDeliveryWorker).GetMethod(
            "ProcessDueDeliveriesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var invocation = method!.Invoke(worker, [cancellationToken]);
        invocation.Should().NotBeNull();
        await (Task)invocation!;
    }

    // ---------------------------------------------------------------------------
    // Private test doubles
    // ---------------------------------------------------------------------------

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _captured;
        private readonly HttpStatusCode _statusCode;

        public CapturingHandler(List<HttpRequestMessage> captured, HttpStatusCode statusCode)
        {
            _captured = captured;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_captured)
            {
                _captured.Add(request);
            }

            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private sealed class CapturingBodyHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task> _onRequest;
        private readonly HttpStatusCode _statusCode;

        public CapturingBodyHandler(Func<HttpRequestMessage, Task> onRequest, HttpStatusCode statusCode)
        {
            _onRequest = onRequest;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await _onRequest(request);
            return new HttpResponseMessage(_statusCode);
        }
    }

    private sealed class FixedClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FixedClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class CapturingDeliveryRepository : IOutboundWebhookDeliveryRepository
    {
        private readonly OutboundWebhookDelivery _delivery;

        public CapturingDeliveryRepository(OutboundWebhookDelivery delivery)
            => _delivery = delivery;

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetDuePendingAsync(
            DateTimeOffset now, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>([_delivery]);

        public Task<bool> TryClaimPendingAsync(
            Guid deliveryId, DateTimeOffset expectedUpdatedAt, DateTimeOffset claimedAt,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task ReloadWithSubscriptionAsync(
            OutboundWebhookDelivery delivery, CancellationToken cancellationToken = default)
        {
            if (delivery.Status == WebhookDeliveryStatus.Pending)
            {
                delivery.MarkProcessing();
            }

            return Task.CompletedTask;
        }

        public Task<OutboundWebhookDelivery?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<OutboundWebhookDelivery?>(_delivery.Id == id ? _delivery : null);

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetStuckProcessingAsync(
            DateTimeOffset staleBefore, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>([]);

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetBySubscriptionAsync(
            Guid subscriptionId, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>([]);

        public Task<IEnumerable<OutboundWebhookDelivery>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<OutboundWebhookDelivery>>([_delivery]);

        public Task<OutboundWebhookDelivery> AddAsync(
            OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);

        public Task UpdateAsync(
            OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(
            OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public StubUnitOfWork(IOutboundWebhookDeliveryRepository deliveries)
            => OutboundWebhookDeliveries = deliveries;

        public IBoardRepository Boards => null!;
        public IColumnRepository Columns => null!;
        public ICardRepository Cards => null!;
        public ICardCommentRepository CardComments => null!;
        public ILabelRepository Labels => null!;
        public IUserRepository Users => null!;
        public IBoardAccessRepository BoardAccesses => null!;
        public IAuditLogRepository AuditLogs => null!;
        public ILlmQueueRepository LlmQueue => null!;
        public IAutomationProposalRepository AutomationProposals => null!;
        public IArchiveItemRepository ArchiveItems => null!;
        public IChatSessionRepository ChatSessions => null!;
        public IChatMessageRepository ChatMessages => null!;
        public ICommandRunRepository CommandRuns => null!;
        public INotificationRepository Notifications => null!;
        public INotificationPreferenceRepository NotificationPreferences => null!;
        public IUserPreferenceRepository UserPreferences => null!;
        public IOutboundWebhookSubscriptionRepository OutboundWebhookSubscriptions => null!;
        public IOutboundWebhookDeliveryRepository OutboundWebhookDeliveries { get; }
        public ILlmUsageRecordRepository LlmUsageRecords => null!;
        public IAgentProfileRepository AgentProfiles => null!;
        public IAgentRunRepository AgentRuns => null!;
        public IKnowledgeDocumentRepository KnowledgeDocuments => null!;
        public IKnowledgeChunkRepository KnowledgeChunks => null!;
        public IExternalLoginRepository ExternalLogins => null!;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
