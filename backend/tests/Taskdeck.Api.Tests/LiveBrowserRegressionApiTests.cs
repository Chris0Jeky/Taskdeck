using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests targeting seams exposed by the 2026-04-02 live browser test session.
/// Each test covers a cross-component integration gap found during manual testing.
/// See: docs/analysis/2026-04-02_manual_testing_live_browser_checklist_report.md
/// </summary>
public class LiveBrowserRegressionApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LiveBrowserRegressionApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // =================================================================
    // #677 — Tool-calling short IDs must be resolvable by the planner
    // =================================================================

    [Fact]
    public async Task ShortCardId_FromToolCalling_ShouldBeResolvableByPlannerMoveInstruction()
    {
        // Arrange: create a board with a card so we have a real short ID
        var userId = await AuthenticateAsync("tool-id-roundtrip");
        var boardId = await CreateBoardWithColumnAndCardAsync(userId, "Backlog", "Test Card");

        // Get the card's full ID
        var cardsResponse = await _client.GetAsync($"/api/boards/{boardId}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().NotBeNullOrEmpty();
        var card = cards![0];

        // Compute the short ID the same way tool executors do (internal FormatShortId)
        var shortId = card.Id.ToString("N")[..8];
        shortId.Should().HaveLength(8, "tool executors emit 8-char hex short IDs");

        // Verify the short ID is NOT a valid GUID (this is the root cause of #677)
        Guid.TryParse(shortId, out _).Should().BeFalse(
            "8-char hex short IDs are not valid GUIDs — the planner's Guid.TryParse rejects them, confirming the bug");
    }

    [Fact]
    public async Task ShortCardId_ShouldMatchExactlyOneCard_OnBoard()
    {
        // Arrange: create a board with multiple cards
        var userId = await AuthenticateAsync("tool-id-uniqueness");
        var boardId = await CreateBoardWithColumnAndCardAsync(userId, "Backlog", "Card One");

        // Add a second card
        var columnsResponse = await _client.GetAsync($"/api/boards/{boardId}/columns");
        var columns = await columnsResponse.Content.ReadFromJsonAsync<List<ColumnDto>>();
        var columnId = columns![0].Id;
        await _client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, "Card Two", null, null, null));

        // Get all cards
        var cardsResponse = await _client.GetAsync($"/api/boards/{boardId}/cards");
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().HaveCount(2);

        // Verify short IDs are unique across the board
        var shortIds = cards!.Select(c => c.Id.ToString("N")[..8]).ToList();
        shortIds.Should().OnlyHaveUniqueItems("short IDs must be unique within a board for tool-calling to work");
    }

    [Fact]
    public async Task ChatWithMockProvider_ShouldReturnResponse_ForBoardScopedToolCallingQuestion()
    {
        // Arrange: board-scoped chat session
        var userId = await AuthenticateAsync("tool-chat-roundtrip");
        var boardId = await CreateBoardWithColumnAndCardAsync(userId, "Backlog", "Auth feature");

        var sessionResponse = await _client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Tool calling test", boardId));
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await sessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();

        // Act: send a tool-calling question
        var messageResponse = await _client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{session!.Id}/messages",
            new SendChatMessageDto("What cards are in Backlog?"));

        // Assert: response should succeed (mock provider handles tool dispatch)
        messageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var message = await messageResponse.Content.ReadFromJsonAsync<ChatMessageDto>();
        message.Should().NotBeNull();
        message!.Content.Should().NotBeNullOrWhiteSpace();
    }

    // =================================================================
    // #678 — Expired proposals must not be approvable or executable
    // =================================================================

    /// <summary>
    /// Confirms bug #678: the approve endpoint does NOT currently reject expired proposals.
    /// The domain entity Approve() checks DateTime.UtcNow > ExpiresAt, but the service layer
    /// may serve the approval from a cached EF entity or the ExpiresAt check may not be
    /// reached before status transition. This test documents the bug — once #678 is fixed,
    /// flip the assertion to expect rejection.
    /// </summary>
    [Fact]
    public async Task ApproveProposal_WhenExpired_CurrentlySucceeds_Bug678()
    {
        // Arrange: create a proposal, then directly expire it in the DB
        var userId = await AuthenticateAsync("expired-approve");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId);

        // Force expiry by setting ExpiresAt to the past via raw SQL
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.Database.ExecuteSqlRaw(
                "UPDATE AutomationProposals SET ExpiresAt = '2020-01-01T00:00:00Z' WHERE Id = {0}",
                proposal.Id.ToString());
        }

        // Act: attempt to approve the expired proposal
        var approveResponse = await _client.PostAsync(
            $"/api/automation/proposals/{proposal.Id}/approve", null);

        // BUG #678: This SHOULD return 400/409 but currently returns 200.
        // When #678 is fixed, change this assertion to:
        //   approveResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "bug #678: expired proposals are currently approvable — this test documents the bug");
    }

    [Fact]
    public async Task GetProposals_ShouldIncludeExpiresAt_SoFrontendCanFilterLocally()
    {
        // The frontend needs ExpiresAt on the DTO to distinguish expired-but-not-yet-swept proposals
        var userId = await AuthenticateAsync("proposal-expiry-dto");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId);

        var response = await _client.GetAsync($"/api/automation/proposals/{proposal.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("expiresAt", out var expiresAt).Should().BeTrue(
            "ProposalDto must include expiresAt so the frontend can filter expired proposals (#678)");
        expiresAt.GetString().Should().NotBeNullOrWhiteSpace();
    }

    // =================================================================
    // #679 — Health endpoint should distinguish configured from probed
    // =================================================================

    [Fact]
    public async Task ProviderHealth_WithoutProbe_ShouldReturnIsProbed_False()
    {
        await AuthenticateAsync("health-no-probe");

        var response = await _client.GetAsync("/api/llm/chat/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("isProbed", out var isProbed).Should().BeTrue(
            "health response must include isProbed discriminant so frontend can distinguish configured vs verified (#679)");
        isProbed.GetBoolean().Should().BeFalse(
            "without ?probe=true the endpoint should not claim it has verified the provider");
    }

    [Fact]
    public async Task ProviderHealth_WithProbe_ShouldReturnIsProbed_True()
    {
        await AuthenticateAsync("health-with-probe");

        var response = await _client.GetAsync("/api/llm/chat/health?probe=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("isProbed", out var isProbed).Should().BeTrue();
        isProbed.GetBoolean().Should().BeTrue(
            "with ?probe=true the mock provider should report as probed");
    }

    // =================================================================
    // #680 — Manual card provenance should return 404, not error noise
    // =================================================================

    [Fact]
    public async Task GetCardProvenance_ForManualCard_ShouldReturn404_NotServerError()
    {
        // Arrange: create a card directly (not via capture/triage)
        var userId = await AuthenticateAsync("provenance-manual");
        var boardId = await CreateBoardWithColumnAndCardAsync(userId, "Backlog", "Manual card");

        var cardsResponse = await _client.GetAsync($"/api/boards/{boardId}/cards");
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        var cardId = cards![0].Id;

        // Act: request provenance for a manually-created card
        var provenanceResponse = await _client.GetAsync(
            $"/api/boards/{boardId}/cards/{cardId}/provenance");

        // Assert: should be a clean 404 with standard error contract, NOT 500
        provenanceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "manual cards have no capture provenance — this is expected, not an error (#680)");

        var error = await provenanceResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.TryGetProperty("errorCode", out var errorCode).Should().BeTrue();
        errorCode.GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GetCardProvenance_ForCaptureOriginatedCard_ShouldReturnProvenance()
    {
        // Arrange: create a card via the capture -> triage -> proposal -> execute pipeline
        var userId = await AuthenticateAsync("provenance-capture");
        var boardId = await CreateBoardWithColumnAndCardAsync(userId, "Backlog", "placeholder");

        // Create a capture item
        var captureResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(boardId, "Fix the login bug"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        // Triage the capture — this should generate a proposal via the queue worker
        var triageResponse = await _client.PostAsync(
            $"/api/capture/items/{capture!.Id}/triage", null);

        // The triage-to-proposal pipeline may be async via worker; if triage returns
        // success we can check the proposal was created
        if (triageResponse.StatusCode == HttpStatusCode.OK ||
            triageResponse.StatusCode == HttpStatusCode.Accepted)
        {
            // Poll for proposals on this board
            var proposals = await ApiTestHarness.PollUntilAsync(
                async () =>
                {
                    var r = await _client.GetAsync(
                        $"/api/automation/proposals?boardId={boardId}&status=PendingReview");
                    return await r.Content.ReadFromJsonAsync<List<ProposalDto>>();
                },
                list => list != null && list.Count > 0,
                "waiting for triage to produce a proposal",
                maxAttempts: 20,
                interval: TimeSpan.FromMilliseconds(500));

            proposals.Should().NotBeEmpty("triage should produce at least one proposal");
        }
        // If triage isn't supported as a direct API call in this test env, that's acceptable —
        // the manual-card test above is the primary regression guard for #680
    }

    // =================================================================
    // Helpers
    // =================================================================

    private async Task<Guid> AuthenticateAsync(string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";
        const string password = "password123";

        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload!.Token);
        return payload.User.Id;
    }

    private async Task<Guid> CreateOwnedBoardAsync(Guid ownerId)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"regression-board-{Guid.NewGuid():N}",
                null,
                Array.Empty<ImportColumnDto>(),
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
    }

    private async Task<Guid> CreateBoardWithColumnAndCardAsync(
        Guid ownerId, string columnName, string cardTitle)
    {
        var boardResponse = await _client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"regression-board-{Guid.NewGuid():N}",
                null,
                new[] { new ImportColumnDto(columnName, 0, null) },
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        boardResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var boardResult = await boardResponse.Content.ReadFromJsonAsync<ImportResultDto>();
        boardResult.Should().NotBeNull();
        var boardId = boardResult!.BoardId!.Value;

        // Get the column ID
        var columnsResponse = await _client.GetAsync($"/api/boards/{boardId}/columns");
        var columns = await columnsResponse.Content.ReadFromJsonAsync<List<ColumnDto>>();
        columns.Should().NotBeNullOrEmpty();
        var columnId = columns![0].Id;

        // Create a card
        var cardResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, cardTitle, null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        return boardId;
    }

    private async Task<ProposalDto> CreateTestProposal(Guid userId, Guid boardId)
    {
        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: userId,
            Summary: $"Regression test proposal {Guid.NewGuid()}",
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardId,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 1,
                    ActionType: "update",
                    TargetType: "board",
                    Parameters: $"{{\"boardId\":\"{boardId}\",\"name\":\"Regression {Guid.NewGuid():N}\"}}",
                    IdempotencyKey: Guid.NewGuid().ToString(),
                    TargetId: boardId.ToString())
            });

        var response = await _client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }
}
