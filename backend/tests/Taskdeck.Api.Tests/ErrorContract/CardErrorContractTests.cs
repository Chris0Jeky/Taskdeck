using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests.ErrorContract;

/// <summary>
/// Verifies GP-03 error contract compliance for card endpoints.
/// Every 4xx response must return a structured ApiErrorResponse with
/// non-empty errorCode and message.
/// </summary>
public class CardErrorContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CardErrorContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a board with a column and returns (boardId, columnId).
    /// Uses the import endpoint to guarantee a column exists.
    /// </summary>
    private async Task<(Guid BoardId, Guid ColumnId)> CreateBoardWithColumnAsync(HttpClient client, string stem)
    {
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, stem);
        var boardDetail = await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}");
        boardDetail.Should().NotBeNull();
        boardDetail!.Columns.Should().NotBeEmpty("imported board should have at least one column");
        return (boardId, boardDetail.Columns.First().Id);
    }

    [Fact]
    public async Task CreateCard_EmptyTitle_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-empty");
        var (boardId, columnId) = await CreateBoardWithColumnAsync(client, "card-err");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, string.Empty, null, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateCard_WhitespaceTitle_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-ws");
        var (boardId, columnId) = await CreateBoardWithColumnAsync(client, "card-ws");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, "   ", null, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateCard_TitleExceeding200Chars_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-long");
        var (boardId, columnId) = await CreateBoardWithColumnAsync(client, "card-long");

        var longTitle = new string('T', 201);
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, longTitle, null, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateCard_NonExistentBoardId_ReturnsForbiddenOrNotFound()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-noboard");

        var fakeBoardId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{fakeBoardId}/cards",
            new CreateCardDto(fakeBoardId, Guid.NewGuid(), "Test Card", null, null, null));

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task CreateCard_NonExistentColumnId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-nocol");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "card-nocol");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, Guid.NewGuid(), "Card In Missing Column", null, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task MoveCard_NonExistentCardId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-movemiss");
        var (boardId, columnId) = await CreateBoardWithColumnAsync(client, "card-movemiss");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards/{Guid.NewGuid()}/move",
            new MoveCardDto(columnId, 0));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task MoveCard_NonExistentTargetColumn_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-movetgt");
        var (boardId, columnId) = await CreateBoardWithColumnAsync(client, "card-movetgt");

        // Create a card first
        var createResponse = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, "Card To Move", null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await createResponse.Content.ReadFromJsonAsync<CardDto>();

        // Move to non-existent column
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards/{card!.Id}/move",
            new MoveCardDto(Guid.NewGuid(), 0));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DeleteCard_NonExistentCardId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-del");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "card-del");

        var response = await client.DeleteAsync(
            $"/api/boards/{board.Id}/cards/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task UpdateCard_NonExistentCardId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-upd");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "card-upd");

        var response = await client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{Guid.NewGuid()}",
            new UpdateCardDto("New Title", null, null, null, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetCards_NonExistentBoardId_ReturnsForbiddenOrNotFound()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-err-getnoboard");

        var response = await client.GetAsync($"/api/boards/{Guid.NewGuid()}/cards");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }
}
