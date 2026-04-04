using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests.ErrorContract;

/// <summary>
/// Verifies GP-03 error contract compliance for label endpoints.
/// Every 4xx response must return a structured ApiErrorResponse with
/// non-empty errorCode and message.
/// </summary>
public class LabelErrorContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LabelErrorContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetLabels_NonExistentBoardId_ReturnsForbiddenOrNotFound()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "label-err-noboard");

        var response = await client.GetAsync($"/api/boards/{Guid.NewGuid()}/labels");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task CreateLabel_EmptyName_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "label-err-empty");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "label-err");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, string.Empty, "#FF0000"));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task UpdateLabel_NonExistentLabelId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "label-err-upd404");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "label-upd");

        var response = await client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/labels/{Guid.NewGuid()}",
            new UpdateLabelDto("New Name", null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteLabel_NonExistentLabelId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "label-err-del404");
        var board = await ApiTestHarness.CreateBoardAsync(client, stem: "label-del");

        var response = await client.DeleteAsync(
            $"/api/boards/{board.Id}/labels/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }
}
