using System.Text.Json;

namespace Taskdeck.Application.Services.Tools;

public sealed class GetBoardCardRelationsExecutor(IBoardRelationService relations) : IToolExecutor
{
    public string ToolName => "get_board_card_relations";

    public Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default) =>
        Task.FromResult(JsonSerializer.Serialize(new { error = "get_board_card_relations requires user context" }, ToolJsonOptions.Default));

    public async Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var result = await relations.GetAsync(context.UserId, context.BoardId, ct);
        return result.IsSuccess
            ? JsonSerializer.Serialize(result.Value, ToolJsonOptions.Default)
            : JsonSerializer.Serialize(new { error = result.ErrorMessage }, ToolJsonOptions.Default);
    }
}
