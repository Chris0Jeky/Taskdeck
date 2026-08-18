using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface ICaptureTriageService
{
    Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromCaptureAsync(
        Guid captureItemId,
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken = default);

    Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromTranscriptAsync(
        Guid captureItemId,
        Guid userId,
        Guid? boardId,
        Guid transcriptId,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken = default);
}
