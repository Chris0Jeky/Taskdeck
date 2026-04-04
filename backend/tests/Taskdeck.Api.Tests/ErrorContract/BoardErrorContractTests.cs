using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests.ErrorContract;

/// <summary>
/// Verifies GP-03 error contract compliance for board endpoints.
/// Every 4xx response must return a structured ApiErrorResponse with
/// non-empty errorCode and message.
/// </summary>
public class BoardErrorContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BoardErrorContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateBoard_EmptyName_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-empty");

        var response = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto(string.Empty, "desc"));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateBoard_WhitespaceName_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-ws");

        var response = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto("   ", "desc"));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateBoard_NameExceeding100Chars_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-long");

        var longName = new string('A', 101);
        var response = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto(longName, "desc"));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateBoard_NameExactly100Chars_ReturnsCreated()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-exact");

        var exactName = new string('B', 100);
        var response = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto(exactName, "desc"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetBoard_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-404");

        var response = await client.GetAsync($"/api/boards/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task UpdateBoard_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-upd404");

        var response = await client.PutAsJsonAsync(
            $"/api/boards/{Guid.NewGuid()}",
            new UpdateBoardDto("new-name", null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DeleteBoard_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-del404");

        var response = await client.DeleteAsync($"/api/boards/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task CreateBoard_SpecialCharactersInName_Succeeds()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-special");

        var response = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto("Board <script>alert('xss')</script>", "desc"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UpdateBoard_EmptyName_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "board-err-updempty");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "update-empty");

        var response = await client.PutAsJsonAsync(
            $"/api/boards/{board.Id}",
            new UpdateBoardDto(string.Empty, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }
}
