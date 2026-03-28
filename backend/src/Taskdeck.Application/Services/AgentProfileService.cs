using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AgentProfileService
{
    private readonly IUnitOfWork _unitOfWork;

    public AgentProfileService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IEnumerable<AgentProfileDto>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profiles = await _unitOfWork.AgentProfiles.GetByUserIdAsync(userId, cancellationToken);
        return Result.Success(profiles.Select(MapToDto));
    }

    public async Task<Result<AgentProfileDto>> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(id, cancellationToken);
        if (profile is null)
            return Result.Failure<AgentProfileDto>(ErrorCodes.NotFound, "Agent profile not found");

        if (profile.UserId != userId)
            return Result.Failure<AgentProfileDto>(ErrorCodes.Forbidden, "Access denied to this agent profile");

        return Result.Success(MapToDto(profile));
    }

    public async Task<Result<AgentProfileDto>> CreateAsync(
        Guid userId,
        CreateAgentProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = new AgentProfile(
                userId,
                dto.Name,
                dto.TemplateKey,
                dto.ScopeType,
                dto.ScopeBoardId,
                dto.Description,
                dto.PolicyJson);

            await _unitOfWork.AgentProfiles.AddAsync(profile, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(profile));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AgentProfileDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<AgentProfileDto>> UpdateAsync(
        Guid id,
        Guid userId,
        UpdateAgentProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(id, cancellationToken);
        if (profile is null)
            return Result.Failure<AgentProfileDto>(ErrorCodes.NotFound, "Agent profile not found");

        if (profile.UserId != userId)
            return Result.Failure<AgentProfileDto>(ErrorCodes.Forbidden, "Access denied to this agent profile");

        try
        {
            profile.UpdateMetadata(dto.Name, dto.Description, dto.PolicyJson);

            if (dto.IsEnabled.HasValue)
                profile.SetEnabled(dto.IsEnabled.Value);

            await _unitOfWork.AgentProfiles.UpdateAsync(profile, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(profile));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AgentProfileDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result> DeleteAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(id, cancellationToken);
        if (profile is null)
            return Result.Failure(ErrorCodes.NotFound, "Agent profile not found");

        if (profile.UserId != userId)
            return Result.Failure(ErrorCodes.Forbidden, "Access denied to this agent profile");

        await _unitOfWork.AgentProfiles.DeleteAsync(profile, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static AgentProfileDto MapToDto(AgentProfile profile)
    {
        return new AgentProfileDto(
            profile.Id,
            profile.UserId,
            profile.Name,
            profile.Description,
            profile.TemplateKey,
            profile.ScopeType,
            profile.ScopeBoardId,
            profile.PolicyJson,
            profile.IsEnabled,
            profile.CreatedAt,
            profile.UpdatedAt);
    }
}
