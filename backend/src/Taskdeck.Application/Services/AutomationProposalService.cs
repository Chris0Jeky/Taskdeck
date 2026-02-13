using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationProposalService : IAutomationProposalService
{
    private readonly IUnitOfWork _unitOfWork;

    public AutomationProposalService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProposalDto>> CreateProposalAsync(CreateProposalDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = new AutomationProposal(
                dto.SourceType,
                dto.RequestedByUserId,
                dto.Summary,
                dto.RiskLevel,
                dto.CorrelationId,
                dto.BoardId,
                dto.SourceReferenceId,
                dto.ExpiryMinutes);

            await _unitOfWork.AutomationProposals.AddAsync(proposal, cancellationToken);

            // Add operations if provided
            if (dto.Operations != null)
            {
                foreach (var opDto in dto.Operations)
                {
                    var operation = new AutomationProposalOperation(
                        proposal.Id,
                        opDto.Sequence,
                        opDto.ActionType,
                        opDto.TargetType,
                        opDto.Parameters,
                        opDto.IdempotencyKey,
                        opDto.TargetId,
                        opDto.ExpectedVersion);

                    proposal.AddOperation(operation);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> GetProposalByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
        if (proposal == null)
            return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

        return Result.Success(MapToDto(proposal));
    }

    public async Task<Result<IEnumerable<ProposalDto>>> GetProposalsAsync(ProposalFilterDto? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new ProposalFilterDto();
        var limit = filter.Limit <= 0 ? 100 : filter.Limit;

        IEnumerable<AutomationProposal> proposals;

        // Apply filters in order of specificity
        if (filter.Status.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByStatusAsync(filter.Status.Value, limit, cancellationToken);
        }
        else if (filter.BoardId.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByBoardIdAsync(filter.BoardId.Value, limit, cancellationToken);
        }
        else if (filter.UserId.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByUserIdAsync(filter.UserId.Value, limit, cancellationToken);
        }
        else if (filter.RiskLevel.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByRiskLevelAsync(filter.RiskLevel.Value, limit, cancellationToken);
        }
        else
        {
            // Get all by status Pending if no filters provided
            proposals = await _unitOfWork.AutomationProposals.GetByStatusAsync(ProposalStatus.PendingReview, limit, cancellationToken);
        }

        // Apply remaining filters in-memory when multiple filters are specified.
        if (filter.Status.HasValue)
            proposals = proposals.Where(p => p.Status == filter.Status.Value);

        if (filter.BoardId.HasValue)
            proposals = proposals.Where(p => p.BoardId == filter.BoardId.Value);

        if (filter.UserId.HasValue)
            proposals = proposals.Where(p => p.RequestedByUserId == filter.UserId.Value);

        if (filter.RiskLevel.HasValue)
            proposals = proposals.Where(p => p.RiskLevel == filter.RiskLevel.Value);

        proposals = proposals.Take(limit);

        return Result.Success(proposals.Select(MapToDto));
    }

    public async Task<Result<ProposalDto>> ApproveProposalAsync(Guid id, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.Approve(decidedByUserId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> RejectProposalAsync(Guid id, Guid decidedByUserId, UpdateProposalStatusDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.Reject(decidedByUserId, dto.Reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> MarkAsAppliedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.MarkAsApplied();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> MarkAsFailedAsync(Guid id, string failureReason, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.MarkAsFailed(failureReason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<int>> ExpireProposalsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var expiredProposals = await _unitOfWork.AutomationProposals.GetExpiredAsync(cancellationToken);
            int count = 0;

            foreach (var proposal in expiredProposals)
            {
                proposal.Expire();
                count++;
            }

            if (count > 0)
                await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(count);
        }
        catch (DomainException ex)
        {
            return Result.Failure<int>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<string>> GetProposalDiffAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
        if (proposal == null)
            return Result.Failure<string>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

        if (string.IsNullOrEmpty(proposal.DiffPreview))
            return Result.Failure<string>(ErrorCodes.NotFound, "Diff preview not available for this proposal");

        return Result.Success(proposal.DiffPreview);
    }

    private static ProposalDto MapToDto(AutomationProposal proposal)
    {
        return new ProposalDto(
            proposal.Id,
            proposal.SourceType,
            proposal.SourceReferenceId,
            proposal.BoardId,
            proposal.RequestedByUserId,
            proposal.Status,
            proposal.RiskLevel,
            proposal.Summary,
            proposal.DiffPreview,
            proposal.ValidationIssues,
            proposal.CreatedAt,
            proposal.UpdatedAt,
            proposal.ExpiresAt,
            proposal.DecidedAt,
            proposal.DecidedByUserId,
            proposal.AppliedAt,
            proposal.FailureReason,
            proposal.CorrelationId,
            proposal.Operations.Select(MapOperationToDto).ToList()
        );
    }

    private static ProposalOperationDto MapOperationToDto(AutomationProposalOperation operation)
    {
        return new ProposalOperationDto(
            operation.Id,
            operation.ProposalId,
            operation.Sequence,
            operation.ActionType,
            operation.TargetType,
            operation.TargetId,
            operation.Parameters,
            operation.IdempotencyKey,
            operation.ExpectedVersion
        );
    }
}
