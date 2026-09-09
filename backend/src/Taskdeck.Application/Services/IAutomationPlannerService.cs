using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public interface IAutomationPlannerService
{
    Task<Result<ProposalDto>> ParseInstructionAsync(
        string instruction,
        Guid userId,
        Guid? boardId = null,
        CancellationToken cancellationToken = default,
        ProposalSourceType sourceType = ProposalSourceType.Manual,
        string? sourceReferenceId = null,
        string? correlationId = null);

    Task<Result<ProposalDto>> ParseInstructionAsync(
        string instruction,
        Guid userId,
        Guid? boardId,
        CancellationToken cancellationToken,
        ProposalSourceType sourceType,
        string? sourceReferenceId,
        string? correlationId,
        ProposalProducerMetadata producerMetadata)
        => ParseInstructionAsync(
            instruction, userId, boardId, cancellationToken, sourceType,
            sourceReferenceId, correlationId);

    /// <summary>
    /// Parses multiple instructions into a single multi-operation proposal.
    /// Each instruction is parsed independently and all resulting operations
    /// are combined into one proposal for atomic review/approve/reject.
    /// Batch size is bounded at <see cref="AutomationPlannerService.MaxBatchSize"/> operations.
    /// </summary>
    Task<Result<ProposalDto>> ParseBatchInstructionAsync(
        IReadOnlyList<string> instructions,
        Guid userId,
        Guid? boardId = null,
        CancellationToken cancellationToken = default,
        ProposalSourceType sourceType = ProposalSourceType.Manual,
        string? sourceReferenceId = null,
        string? correlationId = null);

    Task<Result<ProposalDto>> ParseBatchInstructionAsync(
        IReadOnlyList<string> instructions,
        Guid userId,
        Guid? boardId,
        CancellationToken cancellationToken,
        ProposalSourceType sourceType,
        string? sourceReferenceId,
        string? correlationId,
        ProposalProducerMetadata producerMetadata)
        => ParseBatchInstructionAsync(
            instructions, userId, boardId, cancellationToken, sourceType,
            sourceReferenceId, correlationId);
}
