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

public class CaptureBoardAttachmentAuthorizationApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CaptureBoardAttachmentAuthorizationApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Viewer_CannotCreateCaptureAttachedToReadableBoard()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var owner = await ApiTestHarness.AuthenticateAsync(_client, "cap-owner");
        var board = await ApiTestHarness.CreateBoardAsync(
            _client,
            $"Viewer capture boundary {suffix}");
        var viewer = await ApiTestHarness.AuthenticateAsync(_client, "cap-viewer");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.Add(new BoardAccess(
                board.Id,
                viewer.UserId,
                UserRole.Viewer,
                owner.UserId));
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                "A Viewer must not attach a capture to this board",
                "paste"));

        await ApiTestHarness.AssertErrorContractAsync(
            response,
            HttpStatusCode.Forbidden,
            "Forbidden");
        var captures = await _client.GetFromJsonAsync<List<CaptureItemSummaryDto>>(
            "/api/capture/items");
        captures.Should().BeEmpty(
            "a refused board attachment must not persist a board-scoped capture");
    }

    [Fact]
    public async Task Editor_CanCreateCaptureAttachedToWritableBoard()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var owner = await ApiTestHarness.AuthenticateAsync(_client, "cap-owner");
        var board = await ApiTestHarness.CreateBoardAsync(
            _client,
            $"Editor capture boundary {suffix}");
        var editor = await ApiTestHarness.AuthenticateAsync(_client, "cap-editor");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.Add(new BoardAccess(
                board.Id,
                editor.UserId,
                UserRole.Editor,
                owner.UserId));
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                "An Editor may attach a capture to this board",
                "paste"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();
        capture!.BoardId.Should().Be(board.Id);
        capture.UserId.Should().Be(editor.UserId);
    }
}
