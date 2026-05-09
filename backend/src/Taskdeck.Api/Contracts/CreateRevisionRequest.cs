namespace Taskdeck.Api.Contracts;

public sealed class CreateRevisionRequest
{
    public string RevisedPayload { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
