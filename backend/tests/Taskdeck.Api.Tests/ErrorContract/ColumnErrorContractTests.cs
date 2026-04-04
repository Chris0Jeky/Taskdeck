using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests.ErrorContract;

/// <summary>
/// Verifies GP-03 error contract compliance for column endpoints.
/// Every 4xx response must return a structured ApiErrorResponse with
/// non-empty errorCode and message.
/// </summary>
public class ColumnErrorContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ColumnErrorContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateColumn_EmptyName_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-empty");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-err");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, string.Empty, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateColumn_WhitespaceName_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-ws");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-ws");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "   ", null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateColumn_NameExceeding50Chars_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-long");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-long");

        var longName = new string('C', 51);
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, longName, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateColumn_NameExactly50Chars_Succeeds()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-exact");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-exact");

        var exactName = new string('D', 50);
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, exactName, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateColumn_InvalidWipLimit_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-wip");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-wip");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Bad WIP", null, 0));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateColumn_NegativeWipLimit_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-negwip");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-negwip");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Negative WIP", null, -1));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task UpdateColumn_NonExistentColumnId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-upd404");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-upd404");

        var response = await client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{Guid.NewGuid()}",
            new UpdateColumnDto("New Name", null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteColumn_NonExistentColumnId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-del404");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-del404");

        var response = await client.DeleteAsync(
            $"/api/boards/{board.Id}/columns/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReorderColumns_WithNonExistentColumnId_ReturnsErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-reorder");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "col-reorder");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/reorder",
            new ReorderColumnsDto(new List<Guid> { Guid.NewGuid() }));

        // Non-existent column IDs in reorder request produce an error contract
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        await ApiTestHarness.AssertErrorContractAsync(response, response.StatusCode);
    }

    [Fact]
    public async Task GetColumns_NonExistentBoardId_ReturnsForbiddenOrNotFound()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-err-noboard");

        var response = await client.GetAsync($"/api/boards/{Guid.NewGuid()}/columns");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }
}
