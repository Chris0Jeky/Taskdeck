using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Systematic cross-user data isolation tests.
/// Seeds data via User A, then verifies User B cannot access, list, or mutate it.
/// Covers boards, columns, cards, captures, proposals, notifications, chat sessions,
/// audit trails, knowledge, webhooks, data portability, and the shared board exception.
/// See GitHub issue #704 (TST-37).
/// </summary>
public class CrossUserDataIsolationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CrossUserDataIsolationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Board Layer ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListBoards_UserB_ShouldNotSeeUserA_Boards()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-boards-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-board-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-boards-b");
        var boardB = await ApiTestHarness.CreateBoardAsync(clientB, "iso-board-b");

        var response = await clientB.GetAsync("/api/boards");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var boards = await response.Content.ReadFromJsonAsync<List<BoardDto>>();
        boards.Should().NotBeNull();
        boards!.Should().NotContain(b => b.Id == boardA.Id,
            "User B must not see User A's board in list results");
        boards.Should().Contain(b => b.Id == boardB.Id);
    }

    [Fact]
    public async Task GetBoardById_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-getboard-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-getboard-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-getboard-b");

        var response = await clientB.GetAsync($"/api/boards/{boardA.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task UpdateBoard_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-updateboard-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-updateboard-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-updateboard-b");

        var response = await clientB.PutAsJsonAsync(
            $"/api/boards/{boardA.Id}",
            new UpdateBoardDto("hijacked", null, null));
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task DeleteBoard_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-delboard-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-delboard-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-delboard-b");

        var response = await clientB.DeleteAsync($"/api/boards/{boardA.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Columns on another user's board ──────────────────────────────────────

    [Fact]
    public async Task ListColumns_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-cols-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-cols-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-cols-b");

        var response = await clientB.GetAsync($"/api/boards/{boardA.Id}/columns");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task CreateColumn_UserB_ShouldBeDenied_OnUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-createcol-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-createcol-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-createcol-b");

        var response = await clientB.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/columns",
            new CreateColumnDto(boardA.Id, "Malicious", null, null));
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Cards on another user's board ────────────────────────────────────────

    [Fact]
    public async Task ListCards_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-cards-a");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "iso-cards-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-cards-b");

        var response = await clientB.GetAsync($"/api/boards/{boardId}/cards");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task CreateCard_UserB_ShouldBeDenied_OnUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-createcard-a");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "iso-createcard-a");

        // Get User A's column
        var columnsResponse = await clientA.GetAsync($"/api/boards/{boardId}/columns");
        columnsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var columns = await columnsResponse.Content.ReadFromJsonAsync<List<ColumnDto>>();
        columns.Should().NotBeNullOrEmpty();
        var columnId = columns![0].Id;

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-createcard-b");

        var response = await clientB.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, "Malicious card", null, null, null));
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Capture / Queue ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListCaptures_UserB_ShouldNotSeeUserA_Captures()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-capture-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-capture-a");
        var captureResponse = await clientA.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(boardA.Id, "User A capture note"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var captureA = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-capture-b");

        var response = await clientB.GetAsync("/api/capture/items");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var captures = await response.Content.ReadFromJsonAsync<List<CaptureItemDto>>();
        captures.Should().NotBeNull();
        captures!.Should().NotContain(c => c.Id == captureA!.Id,
            "User B must not see User A's capture items");
    }

    [Fact]
    public async Task GetCaptureById_UserB_ShouldBeDenied_ForUserA_Capture()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-capget-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-capget-a");
        var captureResponse = await clientA.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(boardA.Id, "User A private capture"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var captureA = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-capget-b");

        var response = await clientB.GetAsync($"/api/capture/items/{captureA!.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task LlmQueue_UserB_ShouldNotSeeUserA_QueueItems()
    {
        using var clientA = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "iso-queue-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-queue-a");

        // Seed a queue item for User A
        var createQueueResponse = await clientA.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("triage", "User A private queue payload", boardA.Id));
        createQueueResponse.IsSuccessStatusCode.Should().BeTrue(
            "User A should be able to create a queue item");

        // Verify User A has at least one item
        var userAQueue = await clientA.GetAsync("/api/llm-queue/user");
        userAQueue.StatusCode.Should().Be(HttpStatusCode.OK);
        var userAItems = await userAQueue.Content.ReadFromJsonAsync<List<LlmRequestDto>>();
        userAItems.Should().NotBeNull();
        userAItems!.Count.Should().BeGreaterThan(0, "User A should have at least one queue item");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-queue-b");

        // User B querying their own queue should not contain User A's items
        var response = await clientB.GetAsync("/api/llm-queue/user");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<LlmRequestDto>>();
        items.Should().NotBeNull();
        items!.Should().NotContain(i => i.UserId == userA.UserId,
            "User B must not see queue items belonging to User A");
    }

    // ── Proposals ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListProposals_UserB_ShouldNotSeeUserA_Proposals()
    {
        using var clientA = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "iso-prop-a");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "iso-prop-a");

        // Create a proposal for User A via chat
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(clientA, "iso proposal test", boardId);
        var chatMsgResponse = await clientA.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("create card \"test task\"", RequestProposal: true));
        chatMsgResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify User A can see at least one proposal
        var propResponseA = await clientA.GetAsync("/api/automation/proposals");
        propResponseA.StatusCode.Should().Be(HttpStatusCode.OK);
        var propsA = await propResponseA.Content.ReadFromJsonAsync<List<ProposalDto>>();
        propsA.Should().NotBeNull();
        propsA!.Count.Should().BeGreaterThan(0, "User A should have at least one proposal");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-prop-b");

        var response = await clientB.GetAsync("/api/automation/proposals");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var propsB = await response.Content.ReadFromJsonAsync<List<ProposalDto>>();
        propsB.Should().NotBeNull();
        propsB!.Should().NotContain(p => p.RequestedByUserId == userA.UserId,
            "User B must not see User A's proposals");
    }

    [Fact]
    public async Task GetProposalById_UserB_ShouldBeDenied_ForUserA_Proposal()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-propget-a");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "iso-propget-a");

        var sessionId = await ApiTestHarness.CreateChatSessionAsync(clientA, "iso propget test", boardId);
        var msgResponse = await clientA.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("create card \"isolation test\"", RequestProposal: true));
        var msg = await msgResponse.Content.ReadFromJsonAsync<ChatMessageDto>();
        msg.Should().NotBeNull();
        msg!.ProposalId.Should().NotBeNull();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-propget-b");

        var response = await clientB.GetAsync($"/api/automation/proposals/{msg.ProposalId}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task RejectProposal_UserB_ShouldBeDenied_ForUserA_Proposal()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-propreject-a");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "iso-propreject-a");

        var sessionId = await ApiTestHarness.CreateChatSessionAsync(clientA, "iso propreject test", boardId);
        var msgResponse = await clientA.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("create card \"reject test\"", RequestProposal: true));
        var msg = await msgResponse.Content.ReadFromJsonAsync<ChatMessageDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-propreject-b");

        var response = await clientB.PostAsJsonAsync(
            $"/api/automation/proposals/{msg!.ProposalId}/reject",
            new UpdateProposalStatusDto("unauthorized attempt"));
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task ExecuteProposal_UserB_ShouldBeDenied_ForUserA_Proposal()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-propexec-a");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "iso-propexec-a");

        var sessionId = await ApiTestHarness.CreateChatSessionAsync(clientA, "iso propexec test", boardId);
        var msgResponse = await clientA.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("create card \"exec test\"", RequestProposal: true));
        var msg = await msgResponse.Content.ReadFromJsonAsync<ChatMessageDto>();

        // Approve as User A first
        var approveResponse = await clientA.PostAsJsonAsync(
            $"/api/automation/proposals/{msg!.ProposalId}/approve",
            new UpdateProposalStatusDto());
        approveResponse.IsSuccessStatusCode.Should().BeTrue(
            "User A must be able to approve their own proposal");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-propexec-b");

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/automation/proposals/{msg.ProposalId}/execute");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await clientB.SendAsync(request);
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Notifications ────────────────────────────────────────────────────────

    [Fact]
    public async Task Notifications_UserB_ShouldNotSeeUserA_Notifications()
    {
        using var clientA = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "iso-notif-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-notif-b");

        // Seed a notification for User A via the DB (notifications are system-generated)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.Notifications.Add(new Notification(
                userA.UserId,
                NotificationType.Mention,
                NotificationCadence.Immediate,
                "Cross-user isolation test",
                "This notification belongs to User A.",
                deduplicationKey: $"iso-notif:{Guid.NewGuid():N}"));
            await db.SaveChangesAsync();
        }

        // Verify User A can see their notification
        var userANotifResponse = await clientA.GetAsync("/api/notifications");
        userANotifResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var userANotifs = await userANotifResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();
        userANotifs.Should().NotBeNull();
        userANotifs!.Count.Should().BeGreaterThan(0, "User A should have at least one notification");

        // User B should not see User A's notifications
        var notifResponse = await clientB.GetAsync("/api/notifications");
        notifResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var notifs = await notifResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();
        notifs.Should().NotBeNull();
        notifs!.Should().NotContain(n => n.UserId == userA.UserId,
            "User B must not see User A's notifications");
    }

    [Fact]
    public async Task MarkNotificationAsRead_UserB_ShouldBeDenied_ForUserA_Notification()
    {
        using var clientA = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "iso-notifread-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-notifread-b");

        // Seed a real notification for User A via the DB
        Guid notificationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var notification = new Notification(
                userA.UserId,
                NotificationType.Mention,
                NotificationCadence.Immediate,
                "Mark-read isolation test",
                "This notification belongs to User A.",
                deduplicationKey: $"iso-notifread:{Guid.NewGuid():N}");
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }

        // User B should be denied when trying to mark User A's notification as read
        var response = await clientB.PostAsync($"/api/notifications/{notificationId}/read", null);
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Audit Trail ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditBoardHistory_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-audit-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-audit-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-audit-b");

        var response = await clientB.GetAsync($"/api/audit/boards/{boardA.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Chat Sessions ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListChatSessions_UserB_ShouldNotSeeUserA_Sessions()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-chat-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-chat-a");
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(clientA, "User A session", boardA.Id);

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-chat-b");

        var response = await clientB.GetAsync("/api/llm/chat/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<List<ChatSessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Should().NotContain(s => s.Id == sessionId,
            "User B must not see User A's chat sessions");
    }

    [Fact]
    public async Task GetChatSessionById_UserB_ShouldBeDenied_ForUserA_Session()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-chatget-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-chatget-a");
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(clientA, "Private session", boardA.Id);

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-chatget-b");

        var response = await clientB.GetAsync($"/api/llm/chat/sessions/{sessionId}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task SendMessage_UserB_ShouldBeDenied_ForUserA_Session()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-chatmsg-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-chatmsg-a");
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(clientA, "Msg isolation test", boardA.Id);

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-chatmsg-b");

        var response = await clientB.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("hijack attempt"));
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Knowledge ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListKnowledge_UserB_ShouldNotSeeUserA_Documents()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-know-a");
        var createResponse = await clientA.PostAsJsonAsync(
            "/api/knowledge",
            new CreateKnowledgeDocumentDto("User A doc", "secret content", KnowledgeSourceType.Manual));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var docA = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-know-b");

        var response = await clientB.GetAsync("/api/knowledge");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var docs = await response.Content.ReadFromJsonAsync<List<KnowledgeDocumentDto>>();
        docs.Should().NotBeNull();
        docs!.Should().NotContain(d => d.Id == docA!.Id,
            "User B must not see User A's knowledge documents");
    }

    [Fact]
    public async Task GetKnowledgeById_UserB_ShouldBeDenied_ForUserA_Document()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-knowget-a");
        var createResponse = await clientA.PostAsJsonAsync(
            "/api/knowledge",
            new CreateKnowledgeDocumentDto("Private doc", "private", KnowledgeSourceType.Manual));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var docA = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-knowget-b");

        var response = await clientB.GetAsync($"/api/knowledge/{docA!.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task UpdateKnowledge_UserB_ShouldBeDenied_ForUserA_Document()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-knowupd-a");
        var createResponse = await clientA.PostAsJsonAsync(
            "/api/knowledge",
            new CreateKnowledgeDocumentDto("To update", "original", KnowledgeSourceType.Manual));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var docA = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-knowupd-b");

        var response = await clientB.PutAsJsonAsync(
            $"/api/knowledge/{docA!.Id}",
            new UpdateKnowledgeDocumentDto("hijacked", "evil content"));
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task DeleteKnowledge_UserB_ShouldBeDenied_ForUserA_Document()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-knowdel-a");
        var createResponse = await clientA.PostAsJsonAsync(
            "/api/knowledge",
            new CreateKnowledgeDocumentDto("To delete", "content", KnowledgeSourceType.Manual));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var docA = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-knowdel-b");

        var response = await clientB.DeleteAsync($"/api/knowledge/{docA!.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task SearchKnowledge_UserB_ShouldNotFindUserA_Documents()
    {
        var uniqueToken = $"secrettoken{Guid.NewGuid():N}";

        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-knowsearch-a");
        var createResponse = await clientA.PostAsJsonAsync(
            "/api/knowledge",
            new CreateKnowledgeDocumentDto("Searchable doc", $"Content with {uniqueToken}", KnowledgeSourceType.Manual));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-knowsearch-b");

        var response = await clientB.GetAsync($"/api/knowledge/search?q={uniqueToken}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<List<KnowledgeSearchResultDto>>();
        results.Should().NotBeNull();
        results!.Should().BeEmpty(
            "User B's knowledge search must not return User A's documents");
    }

    // ── Webhooks ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListWebhooks_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-webhook-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-webhook-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-webhook-b");

        var response = await clientB.GetAsync($"/api/boards/{boardA.Id}/webhooks");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task CreateWebhook_UserB_ShouldBeDenied_OnUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-webhookcreate-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-webhookcreate-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-webhookcreate-b");

        var response = await clientB.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/webhooks",
            new CreateOutboundWebhookSubscriptionDto("https://evil.example.com/hook"));
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Export (board-level) ─────────────────────────────────────────────────

    [Fact]
    public async Task ExportBoard_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-export-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-export-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-export-b");

        var response = await clientB.GetAsync($"/api/export/boards/{boardA.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task ExportBoardJson_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-exportjson-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-exportjson-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-exportjson-b");

        var response = await clientB.GetAsync($"/api/export/boards/{boardA.Id}/json");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Board Access Controls ────────────────────────────────────────────────

    [Fact]
    public async Task BoardAccess_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-access-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-access-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-access-b");

        var response = await clientB.GetAsync($"/api/boards/{boardA.Id}/access");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Shared Board Exception ───────────────────────────────────────────────

    [Fact]
    public async Task SharedBoard_UserB_CanAccess_WhenGrantedExplicitAccess()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-share-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-share-a");

        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "iso-share-b");

        // Before sharing: User B cannot access
        var beforeResponse = await clientB.GetAsync($"/api/boards/{boardA.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(beforeResponse);

        // Grant User B access
        var grantResponse = await clientA.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/access",
            new GrantAccessDto(boardA.Id, userB.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // After sharing: User B CAN access
        var afterResponse = await clientB.GetAsync($"/api/boards/{boardA.Id}");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SharedBoard_UserB_CannotAccessOtherBoards_WhenGrantedAccessToOne()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-shareother-a");
        var sharedBoard = await ApiTestHarness.CreateBoardAsync(clientA, "iso-shared");
        var privateBoard = await ApiTestHarness.CreateBoardAsync(clientA, "iso-private");

        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "iso-shareother-b");

        // Grant User B access only to the shared board
        var grantResponse = await clientA.PostAsJsonAsync(
            $"/api/boards/{sharedBoard.Id}/access",
            new GrantAccessDto(sharedBoard.Id, userB.UserId, UserRole.Viewer));
        grantResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // User B can access shared board
        var sharedResponse = await clientB.GetAsync($"/api/boards/{sharedBoard.Id}");
        sharedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // User B CANNOT access the private board
        var privateResponse = await clientB.GetAsync($"/api/boards/{privateBoard.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(privateResponse);
    }

    [Fact]
    public async Task SharedBoard_RevokedAccess_UserB_ImmediatelyLosesAccess()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-revoke-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-revoke-a");

        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "iso-revoke-b");

        // Grant access
        var grantResponse = await clientA.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/access",
            new GrantAccessDto(boardA.Id, userB.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Get the access entry to find the access ID
        var accessListResponse = await clientA.GetAsync($"/api/boards/{boardA.Id}/access");
        accessListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var accessList = await accessListResponse.Content.ReadFromJsonAsync<List<BoardAccessDto>>();
        var userBAccess = accessList!.FirstOrDefault(a => a.UserId == userB.UserId);
        userBAccess.Should().NotBeNull("User B should have an access entry");

        // Verify User B can access
        var canAccessResponse = await clientB.GetAsync($"/api/boards/{boardA.Id}");
        canAccessResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Revoke access
        var revokeResponse = await clientA.DeleteAsync(
            $"/api/boards/{boardA.Id}/access/{userBAccess!.Id}");
        revokeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // User B should immediately lose access
        var afterRevokeResponse = await clientB.GetAsync($"/api/boards/{boardA.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(afterRevokeResponse);
    }

    // ── Labels on another user's board ───────────────────────────────────────

    [Fact]
    public async Task ListLabels_UserB_ShouldBeDenied_ForUserA_Board()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-labels-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-labels-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-labels-b");

        var response = await clientB.GetAsync($"/api/boards/{boardA.Id}/labels");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Capture mutation isolation ───────────────────────────────────────────

    [Fact]
    public async Task IgnoreCapture_UserB_ShouldBeDenied_ForUserA_Capture()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-capignore-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-capignore-a");
        var captureResponse = await clientA.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(boardA.Id, "ignore test capture"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var captureA = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-capignore-b");

        var response = await clientB.PostAsync($"/api/capture/items/{captureA!.Id}/ignore", null);
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task TriageCapture_UserB_ShouldBeDenied_ForUserA_Capture()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-captriage-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "iso-captriage-a");
        var captureResponse = await clientA.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(boardA.Id, "triage test capture"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var captureA = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-captriage-b");

        var response = await clientB.PostAsync($"/api/capture/items/{captureA!.Id}/triage", null);
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    // ── Proposal diff isolation ──────────────────────────────────────────────

    [Fact]
    public async Task ProposalDiff_UserB_ShouldBeDenied_ForUserA_Proposal()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "iso-propdiff-a");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "iso-propdiff-a");

        var sessionId = await ApiTestHarness.CreateChatSessionAsync(clientA, "diff isolation test", boardId);
        var msgResponse = await clientA.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("create card \"diff test\"", RequestProposal: true));
        var msg = await msgResponse.Content.ReadFromJsonAsync<ChatMessageDto>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "iso-propdiff-b");

        var response = await clientB.GetAsync($"/api/automation/proposals/{msg!.ProposalId}/diff");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }
}
