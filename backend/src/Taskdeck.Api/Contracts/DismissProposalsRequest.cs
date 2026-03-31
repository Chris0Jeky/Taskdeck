namespace Taskdeck.Api.Contracts;

/// <summary>
/// Request body for the batch dismiss proposals endpoint.
/// </summary>
public sealed class DismissProposalsRequest
{
    public IReadOnlyList<Guid>? Ids { get; set; }
}
