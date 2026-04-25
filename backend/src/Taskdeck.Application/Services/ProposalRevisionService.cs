using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ProposalRevisionService : IProposalRevisionService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProposalRevisionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProposalRevisionDto>> CreateRevisionAsync(
        CreateProposalRevisionDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(dto.ProposalId, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalRevisionDto>(ErrorCodes.NotFound, "Proposal not found");

            if (proposal.Status != ProposalStatus.PendingReview)
                return Result.Failure<ProposalRevisionDto>(
                    ErrorCodes.InvalidOperation,
                    $"Cannot create revision for proposal in status {proposal.Status}");

            var nextRevisionNumber = await _unitOfWork.ProposalRevisions
                .GetNextRevisionNumberAsync(dto.ProposalId, cancellationToken);

            var revision = new ProposalRevision(
                dto.ProposalId,
                nextRevisionNumber,
                dto.EditorUserId,
                dto.RevisedPayload,
                dto.Reason);

            await _unitOfWork.ProposalRevisions.AddAsync(revision, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(revision));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalRevisionDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<ProposalRevisionDto>>> GetRevisionsForProposalAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal == null)
            return Result.Failure<IReadOnlyList<ProposalRevisionDto>>(ErrorCodes.NotFound, "Proposal not found");

        var revisions = await _unitOfWork.ProposalRevisions
            .GetByProposalIdAsync(proposalId, cancellationToken);

        var dtos = revisions.Select(MapToDto).ToList() as IReadOnlyList<ProposalRevisionDto>;
        return Result.Success(dtos);
    }

    public async Task<Result<ProposalRevisionDto?>> GetLatestRevisionAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal == null)
            return Result.Failure<ProposalRevisionDto?>(ErrorCodes.NotFound, "Proposal not found");

        var latest = await _unitOfWork.ProposalRevisions
            .GetLatestByProposalIdAsync(proposalId, cancellationToken);

        return Result.Success(latest != null ? MapToDto(latest) : (ProposalRevisionDto?)null);
    }

    private static ProposalRevisionDto MapToDto(ProposalRevision revision)
    {
        return new ProposalRevisionDto(
            revision.Id,
            revision.ProposalId,
            revision.RevisionNumber,
            revision.EditorUserId,
            revision.RevisedPayload,
            revision.RevisedAt,
            revision.Reason,
            revision.CreatedAt);
    }
}
