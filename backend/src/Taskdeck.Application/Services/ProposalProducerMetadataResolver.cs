using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

internal static class ProposalProducerMetadataResolver
{
    public static ProposalProducerMetadata? FromDispatchedRequest(LlmDispatchContext dispatchContext)
    {
        var snapshot = dispatchContext.ReadSnapshot();
        if (snapshot.Phase != LlmDispatchPhase.Dispatched ||
            string.IsNullOrWhiteSpace(snapshot.Provider) ||
            string.IsNullOrWhiteSpace(snapshot.Model) ||
            string.Equals(snapshot.Provider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ProposalProducerMetadata(
            snapshot.Provider.Trim(),
            snapshot.Model.Trim());
    }
}
