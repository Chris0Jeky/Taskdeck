using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface ICaptureTriageService
{
    /// <summary>
    /// Triages a capture into a reviewable proposal. <paramref name="anchor"/> is the capture's own
    /// calendar day and is what partial dates in the text resolve against (#2193); a caller holding
    /// the capture row passes <see cref="CaptureTriageAnchor.FromCapture"/> over its server-stamped
    /// <c>CreatedAt</c>, so a delayed or retried run still anchors to when the capture happened.
    /// Null falls back to <see cref="CaptureTriageAnchor.ForImmediateTriage"/>.
    /// </summary>
    Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromCaptureAsync(
        Guid captureItemId,
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        CaptureTriageAnchor? anchor = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="CreateProposalFromCaptureAsync"/>
    Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromTranscriptAsync(
        Guid captureItemId,
        Guid userId,
        Guid? boardId,
        Guid transcriptId,
        CapturePayloadV1 payload,
        CaptureTriageAnchor? anchor = null,
        CancellationToken cancellationToken = default);
}
