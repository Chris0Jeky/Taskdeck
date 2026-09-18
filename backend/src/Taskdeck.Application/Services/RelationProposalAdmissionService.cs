using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Proposal-service decorator that makes typed relation validation producer-neutral.
///
/// MCP and chat may still preflight relation operations to return transport-specific explanations,
/// but every producer ultimately crosses this write bar before a proposal row is staged. Non-relation
/// proposals retain the existing generic admission contract unchanged.
/// </summary>
public sealed class RelationProposalAdmissionService(
    AutomationProposalService inner,
    IAutomationPolicyEngine policyEngine) : IAutomationProposalService
{
    public async Task<Result<ProposalDto>> CreateProposalAsync(
        CreateProposalDto dto,
        CancellationToken cancellationToken = default)
    {
        var admission = await ValidateTypedRelationsAsync(dto, cancellationToken);
        return admission.IsSuccess
            ? await inner.CreateProposalAsync(dto, cancellationToken)
            : Result.Failure<ProposalDto>(admission.ErrorCode, admission.ErrorMessage);
    }

    public async Task<Result<ProposalDto>> CreateTranscriptProposalAsync(
        CreateProposalDto dto,
        IReadOnlyList<TranscriptEvidenceLinkInput> evidence,
        CancellationToken cancellationToken = default)
    {
        var admission = await ValidateTypedRelationsAsync(dto, cancellationToken);
        return admission.IsSuccess
            ? await inner.CreateTranscriptProposalAsync(dto, evidence, cancellationToken)
            : Result.Failure<ProposalDto>(admission.ErrorCode, admission.ErrorMessage);
    }

    private async Task<Result> ValidateTypedRelationsAsync(
        CreateProposalDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.Operations is not { Count: > 0 } operations ||
            !operations.Any(IsTypedRelationOperation))
        {
            return Result.Success();
        }

        // Map the complete ordered plan, not only the relation rows. The shared contract validator
        // uses preceding create-card operations to admit references to endpoints that will exist by
        // the time Apply reaches the relation operation. Empty ids are safe here: admission reasons
        // exclusively over sequence, target, parameters and expected-version material.
        var effectiveOperations = operations
            .Select(operation => new ProposalOperationDto(
                Guid.Empty,
                Guid.Empty,
                operation.Sequence,
                operation.ActionType,
                operation.TargetType,
                operation.TargetId,
                operation.Parameters,
                operation.IdempotencyKey,
                operation.ExpectedVersion))
            .ToArray();

        var structure = policyEngine.ValidateOperationStructure(effectiveOperations);
        if (!structure.IsSuccess)
            return structure;

        return await policyEngine.ValidatePermissionsAsync(
            dto.RequestedByUserId,
            dto.BoardId,
            effectiveOperations,
            BoardAccessBar.Write,
            cancellationToken);
    }

    private static bool IsTypedRelationOperation(CreateProposalOperationDto operation) =>
        operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        (operation.ActionType.Equals("add-relation", StringComparison.OrdinalIgnoreCase) ||
         operation.ActionType.Equals("remove-relation", StringComparison.OrdinalIgnoreCase));

    public Task<Result<ProposalDto>> GetProposalByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.GetProposalByIdAsync(id, cancellationToken);

    public Task<Result<IEnumerable<ProposalDto>>> GetProposalsAsync(
        ProposalFilterDto? filter = null,
        CancellationToken cancellationToken = default) =>
        inner.GetProposalsAsync(filter, cancellationToken);

    public Task<Result<ProposalDto>> ApproveProposalAsync(
        Guid id,
        Guid decidedByUserId,
        CancellationToken cancellationToken = default) =>
        inner.ApproveProposalAsync(id, decidedByUserId, cancellationToken);

    public Task<Result<BatchApproveProposalsResultDto>> ApproveProposalsAsync(
        IReadOnlyList<BatchApproveProposalSelectionDto> proposals,
        Guid decidedByUserId,
        CancellationToken cancellationToken = default) =>
        inner.ApproveProposalsAsync(proposals, decidedByUserId, cancellationToken);

    public Task<Result<ProposalDto>> RejectProposalAsync(
        Guid id,
        Guid decidedByUserId,
        UpdateProposalStatusDto dto,
        CancellationToken cancellationToken = default) =>
        inner.RejectProposalAsync(id, decidedByUserId, dto, cancellationToken);

    public Task<Result<ProposalDto>> DeferProposalAsync(
        Guid id,
        TimeSpan duration,
        CancellationToken cancellationToken = default) =>
        inner.DeferProposalAsync(id, duration, cancellationToken);

    public Task<Result<ProposalDto>> MarkAsAppliedAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.MarkAsAppliedAsync(id, cancellationToken);

    public Task<Result<ProposalDto>> MarkAsFailedAsync(
        Guid id,
        string failureReason,
        CancellationToken cancellationToken = default) =>
        inner.MarkAsFailedAsync(id, failureReason, cancellationToken);

    public Task<Result<int>> ExpireProposalsAsync(
        CancellationToken cancellationToken = default) =>
        inner.ExpireProposalsAsync(cancellationToken);

    public Task<Result<string>> GetProposalDiffAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.GetProposalDiffAsync(id, cancellationToken);

    public Task<Result<ProposalPreviewDto>> GetProposalPreviewAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.GetProposalPreviewAsync(id, cancellationToken);

    public Task<Result<string>> GetTerminalProposalStoredPreviewAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.GetTerminalProposalStoredPreviewAsync(id, cancellationToken);

    public Task<Result<int>> DismissProposalsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default) =>
        inner.DismissProposalsAsync(ids, cancellationToken);
}
