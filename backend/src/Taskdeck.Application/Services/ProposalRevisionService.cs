using System.Text.Json;
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

            var payloadValidation = ProposalRevisionPayload.TryParseOperations(
                dto.ProposalId,
                dto.RevisedPayload,
                out _,
                out var validationError);
            if (!payloadValidation)
                return Result.Failure<ProposalRevisionDto>(
                    ErrorCodes.ValidationError,
                    validationError);

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

    // NOTE: There is a narrow race window between GetNextRevisionNumberAsync and
    // SaveChangesAsync. If two concurrent callers both read the same next revision
    // number, the DB unique constraint on (ProposalId, RevisionNumber) rejects one
    // write and UnitOfWork maps that persistence collision to DomainException(Conflict),
    // which is caught above and returned as a non-500 retryable result.

    public async Task<Result<IReadOnlyList<ProposalRevisionDto>>> GetRevisionsForProposalAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal == null)
            return Result.Failure<IReadOnlyList<ProposalRevisionDto>>(ErrorCodes.NotFound, "Proposal not found");

        var revisions = await _unitOfWork.ProposalRevisions
            .GetByProposalIdAsync(proposalId, cancellationToken);

        var dtos = revisions.Select(MapToDto).ToList();
        return Result.Success<IReadOnlyList<ProposalRevisionDto>>(dtos);
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

internal static class ProposalRevisionPayload
{
    public static bool TryParseOperations(
        Guid proposalId,
        string payload,
        out List<ProposalOperationDto> operations,
        out string errorMessage)
    {
        operations = new List<ProposalOperationDto>();
        errorMessage = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "RevisedPayload must be a JSON object";
                return false;
            }

            if (!document.RootElement.TryGetProperty("operations", out var operationsElement))
            {
                errorMessage = "RevisedPayload must contain an operations array";
                return false;
            }

            if (operationsElement.ValueKind != JsonValueKind.Array)
            {
                errorMessage = "RevisedPayload operations must be an array";
                return false;
            }

            foreach (var operationElement in operationsElement.EnumerateArray())
            {
                if (!TryParseOperation(proposalId, operationElement, out var operation, out errorMessage))
                    return false;

                operations.Add(operation);
            }

            if (operations.Count == 0)
            {
                errorMessage = "RevisedPayload operations must contain at least one operation";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            errorMessage = "RevisedPayload must be valid JSON";
            return false;
        }
    }

    private static bool TryParseOperation(
        Guid proposalId,
        JsonElement operationElement,
        out ProposalOperationDto operation,
        out string errorMessage)
    {
        operation = default!;
        errorMessage = string.Empty;

        if (operationElement.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "Each revised operation must be a JSON object";
            return false;
        }

        if (!TryGetInt32(operationElement, "sequence", out var sequence, out errorMessage))
            return false;

        if (sequence < 0)
        {
            errorMessage = "Revised operation sequence must be non-negative";
            return false;
        }

        if (!TryGetRequiredString(operationElement, "actionType", out var actionType, out errorMessage))
            return false;

        if (!TryGetRequiredString(operationElement, "targetType", out var targetType, out errorMessage))
            return false;

        if (!TryGetRequiredString(operationElement, "parameters", out var parameters, out errorMessage))
            return false;

        if (!IsValidJsonObject(parameters))
        {
            errorMessage = "Revised operation parameters must be a JSON object string";
            return false;
        }

        if (!TryGetRequiredString(operationElement, "idempotencyKey", out var idempotencyKey, out errorMessage))
            return false;

        var operationId = TryGetOptionalGuid(operationElement, "id") ?? Guid.Empty;
        var targetId = TryGetOptionalString(operationElement, "targetId");
        var expectedVersion = TryGetOptionalString(operationElement, "expectedVersion");

        operation = new ProposalOperationDto(
            operationId,
            proposalId,
            sequence,
            actionType,
            targetType,
            targetId,
            parameters,
            idempotencyKey,
            expectedVersion);
        return true;
    }

    private static bool TryGetInt32(
        JsonElement element,
        string propertyName,
        out int value,
        out string errorMessage)
    {
        value = default;
        errorMessage = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out value))
        {
            errorMessage = $"Revised operation {propertyName} must be an integer";
            return false;
        }

        return true;
    }

    private static bool TryGetRequiredString(
        JsonElement element,
        string propertyName,
        out string value,
        out string errorMessage)
    {
        value = string.Empty;
        errorMessage = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            errorMessage = $"Revised operation {propertyName} must be a string";
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = $"Revised operation {propertyName} cannot be empty";
            return false;
        }

        return true;
    }

    private static string? TryGetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null ||
            property.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static Guid? TryGetOptionalGuid(JsonElement element, string propertyName)
    {
        var value = TryGetOptionalString(element, propertyName);
        return Guid.TryParse(value, out var guid) ? guid : null;
    }

    private static bool IsValidJsonObject(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
