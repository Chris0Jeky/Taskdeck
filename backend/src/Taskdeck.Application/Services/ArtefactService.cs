using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class ArtefactService : IArtefactService
{
    private readonly ISourceArtefactRepository _artefacts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ArtefactStorageSettings _settings;

    public ArtefactService(
        ISourceArtefactRepository artefacts,
        IUnitOfWork unitOfWork,
        ArtefactStorageSettings settings)
    {
        _artefacts = artefacts;
        _unitOfWork = unitOfWork;
        _settings = settings;
    }

    public async Task<Result<SourceArtefactDto>> CreateAsync(
        Guid userId,
        CreateArtefactRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<SourceArtefactDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (request.BoardId.HasValue &&
            !await _unitOfWork.BoardAccesses.HasAccessAsync(
                request.BoardId.Value,
                userId,
                UserRole.Editor,
                cancellationToken))
        {
            return Result.Failure<SourceArtefactDto>(ErrorCodes.Forbidden, "Editor access to the board is required");
        }

        if (request.CreatedFromCaptureId.HasValue)
        {
            var capture = await _unitOfWork.LlmQueue.GetByIdAsync(request.CreatedFromCaptureId.Value, cancellationToken);
            if (capture is null || capture.UserId != userId)
            {
                return Result.Failure<SourceArtefactDto>(
                    ErrorCodes.Forbidden,
                    "The linked capture does not belong to the authenticated user");
            }
        }

        var validation = await ArtefactContentValidator.ReadAndValidateAsync(
            request.Content,
            request.FileName,
            request.MimeType,
            _settings.MaxBytesPerArtefact,
            cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.Failure<SourceArtefactDto>(validation.ErrorCode, validation.ErrorMessage);
        }

        var content = validation.Value;
        var artefact = new SourceArtefact(
            userId,
            content.Kind,
            content.MimeType,
            content.FileName,
            content.Bytes.LongLength,
            content.Sha256,
            CaptureSource.Import,
            request.BoardId,
            createdFromCaptureId: request.CreatedFromCaptureId);
        var auditLog = new AuditLog(
            "SourceArtefact",
            artefact.Id,
            AuditAction.Created,
            userId,
            $"kind={artefact.Kind}; bytes={artefact.ByteSize}");

        var stored = await _artefacts.TryAddWithinQuotaAsync(
            artefact,
            content.Bytes,
            _settings.MaxBytesPerUser,
            auditLog,
            cancellationToken);
        if (!stored)
        {
            return Result.Failure<SourceArtefactDto>(
                ErrorCodes.PayloadTooLarge,
                $"Artefact would exceed the configured {_settings.MaxBytesPerUser}-byte user quota");
        }

        return Result.Success(Map(artefact));
    }

    public async Task<Result<SourceArtefactDto>> GetMetadataAsync(
        Guid userId,
        Guid artefactId,
        CancellationToken cancellationToken = default)
    {
        var artefact = await _artefacts.GetByIdForUserAsync(artefactId, userId, cancellationToken);
        return artefact is null
            ? Result.Failure<SourceArtefactDto>(ErrorCodes.NotFound, "Artefact not found")
            : Result.Success(Map(artefact));
    }

    public async Task<Result> CopyContentAsync(
        Guid userId,
        Guid artefactId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var copied = await _artefacts.CopyContentForUserAsync(
            artefactId,
            userId,
            destination,
            cancellationToken);
        return copied
            ? Result.Success()
            : Result.Failure(ErrorCodes.NotFound, "Artefact not found");
    }

    public async Task<Result> DeleteAsync(
        Guid userId,
        Guid artefactId,
        CancellationToken cancellationToken = default)
    {
        var auditLog = new AuditLog(
            "SourceArtefact",
            artefactId,
            AuditAction.Deleted,
            userId,
            "Source artefact deleted");
        var deleted = await _artefacts.DeleteWithAuditAsync(
            artefactId,
            userId,
            auditLog,
            cancellationToken);
        return deleted
            ? Result.Success()
            : Result.Failure(ErrorCodes.NotFound, "Artefact not found");
    }

    private static SourceArtefactDto Map(SourceArtefact artefact)
        => new(
            artefact.Id,
            artefact.BoardId,
            artefact.Kind,
            artefact.MimeType,
            artefact.FileName,
            artefact.ByteSize,
            artefact.Sha256,
            artefact.CaptureSource,
            artefact.OriginReference,
            artefact.CreatedFromCaptureId,
            artefact.CreatedAt);
}
