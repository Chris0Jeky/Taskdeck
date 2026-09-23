using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ArchivedBoardResourceApiTests
{
    [Theory]
    [InlineData("column-create")]
    [InlineData("column-update")]
    [InlineData("column-delete")]
    [InlineData("column-reorder")]
    [InlineData("label-create")]
    [InlineData("label-update")]
    [InlineData("label-delete")]
    public async Task ArchivedBoard_RejectsResourceWritesUntilRestore(string operation)
    {
        using var factory = new HostedWorkerDisabledTestWebApplicationFactory();
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "archive-resources");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-resource-board");
        var columnResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Original", null, 3));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await columnResponse.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();
        var secondColumn = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Second", null, 5));
        secondColumn.StatusCode.Should().Be(HttpStatusCode.Created);
        var labelResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "Original", "#123456"));
        labelResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var label = await labelResponse.Content.ReadFromJsonAsync<LabelDto>();
        label.Should().NotBeNull();
        var columnsBefore = await client.GetFromJsonAsync<List<ColumnDto>>($"/api/boards/{board.Id}/columns");
        var labelsBefore = await client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        columnsBefore.Should().NotBeNull();
        var reorderIds = columnsBefore!.Select(item => item.Id).Reverse().ToList();
        var archive = await client.PutAsJsonAsync($"/api/boards/{board.Id}", new UpdateBoardDto(null, null, true));
        archive.StatusCode.Should().Be(HttpStatusCode.OK);

        var denied = await MutateAsync(client, operation, board.Id, column!.Id, label!.Id, reorderIds);
        await ApiTestHarness.AssertErrorContractAsync(denied, HttpStatusCode.BadRequest, "InvalidOperation");
        (await denied.Content.ReadAsStringAsync()).Should().Contain("Restore the board");
        var columnsAfter = await client.GetFromJsonAsync<List<ColumnDto>>($"/api/boards/{board.Id}/columns");
        var labelsAfter = await client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        columnsAfter.Should().BeEquivalentTo(columnsBefore);
        labelsAfter.Should().BeEquivalentTo(labelsBefore);

        // Existing controller authorization must still run before archive validation.
        using var anonymous = factory.CreateClient();
        await ApiTestHarness.AssertUnauthorizedAsync(
            await MutateAsync(anonymous, operation, board.Id, column.Id, label.Id, reorderIds));
        using var outsider = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(outsider, "archive-outsider");
        var forbidden = await MutateAsync(outsider, operation, board.Id, column.Id, label.Id, reorderIds);
        await ApiTestHarness.AssertForbiddenAsync(forbidden);
        (await forbidden.Content.ReadAsStringAsync()).Should().NotContain("archived");

        var restore = await client.PutAsJsonAsync($"/api/boards/{board.Id}", new UpdateBoardDto(null, null, false));
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
        var allowed = await MutateAsync(client, operation, board.Id, column.Id, label.Id, reorderIds);
        allowed.IsSuccessStatusCode.Should().BeTrue(await allowed.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> MutateAsync(
        HttpClient client, string operation, Guid boardId, Guid columnId, Guid labelId, List<Guid> reorderIds)
    {
        var columns = $"/api/boards/{boardId}/columns";
        var labels = $"/api/boards/{boardId}/labels";
        return operation switch
        {
            "column-create" => client.PostAsJsonAsync(columns, new CreateColumnDto(boardId, "New", null, null)),
            "column-update" => client.PatchAsJsonAsync($"{columns}/{columnId}", new UpdateColumnDto("After", null, 8)),
            "column-delete" => client.DeleteAsync($"{columns}/{columnId}"),
            "column-reorder" => client.PostAsJsonAsync($"{columns}/reorder", new ReorderColumnsDto(reorderIds)),
            "label-create" => client.PostAsJsonAsync(labels, new CreateLabelDto(boardId, "New", "#abcdef")),
            "label-update" => client.PatchAsJsonAsync($"{labels}/{labelId}", new UpdateLabelDto("After", "#abcdef")),
            "label-delete" => client.DeleteAsync($"{labels}/{labelId}"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown test operation")
        };
    }
}
