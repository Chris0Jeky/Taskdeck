namespace Taskdeck.Api.Hubs;

public static class BoardHubGroups
{
    public static string ForBoard(Guid boardId)
    {
        return $"board:{boardId:N}";
    }
}
