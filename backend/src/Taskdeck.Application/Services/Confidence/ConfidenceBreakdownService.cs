using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Confidence;

/// <summary>
/// Reads confidence that was actually persisted with proposal provenance. It never synthesizes
/// confidence from risk, operation count, recency, or action names.
/// </summary>
public sealed class ConfidenceBreakdownService : IConfidenceBreakdownService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProposalProvenanceRepository _provenanceRepository;

    public ConfidenceBreakdownService(
        IUnitOfWork unitOfWork,
        IProposalProvenanceRepository provenanceRepository)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _provenanceRepository = provenanceRepository ?? throw new ArgumentNullException(nameof(provenanceRepository));
    }

    /// <inheritdoc />
    public async Task<Result<ConfidenceBreakdownDto>> GetBreakdownAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal is null)
        {
            return Result.Failure<ConfidenceBreakdownDto>(
                ErrorCodes.NotFound,
                $"Proposal {proposalId} not found.");
        }

        var provenance = await _provenanceRepository.GetByProposalIdAsync(proposalId, cancellationToken);
        if (provenance is null)
            return Result.Success(NotReported());

        var modelReported = provenance.Fields
            .Where(field =>
                field.ConfidenceSource == ProvenanceConfidenceSource.ModelReported &&
                field.Confidence.HasValue)
            .OrderBy(field => OperationOrdinal(field.FieldName))
            .ThenBy(field => field.FieldName, StringComparer.Ordinal)
            .Select(field => new ConfidenceComponentDto(field.FieldName, field.Confidence!.Value))
            .ToList();

        if (modelReported.Count > 0)
        {
            return Result.Success(new ConfidenceBreakdownDto(
                Overall: modelReported.Average(component => component.Value),
                Components: modelReported,
                Note: "Average and item values are model-reported confidence only; review, approval, and Apply remain explicit.",
                Threshold: null,
                Source: ConfidenceBreakdownDto.ModelReportedSource));
        }

        var derived = provenance.Fields
            .Where(field =>
                field.ConfidenceSource == ProvenanceConfidenceSource.Derived &&
                field.Confidence.HasValue)
            .OrderBy(field => OperationOrdinal(field.FieldName))
            .ThenBy(field => field.FieldName, StringComparer.Ordinal)
            .Select(field => new ConfidenceComponentDto(field.FieldName, field.Confidence!.Value))
            .ToList();
        if (derived.Count > 0)
        {
            return Result.Success(new ConfidenceBreakdownDto(
                Overall: derived.Average(component => component.Value),
                Components: derived,
                Note: "These values were derived by a verification algorithm, not reported by a model.",
                Threshold: null,
                Source: ConfidenceBreakdownDto.DerivedSource));
        }

        if (provenance.Fields.Any(field =>
                field.ConfidenceSource == ProvenanceConfidenceSource.Deterministic))
        {
            return Result.Success(new ConfidenceBreakdownDto(
                Overall: null,
                Components: Array.Empty<ConfidenceComponentDto>(),
                Note: "Deterministic extraction produced no model confidence value.",
                Threshold: null,
                Source: ConfidenceBreakdownDto.DeterministicSource));
        }

        return Result.Success(NotReported());
    }

    private static int OperationOrdinal(string fieldName)
    {
        const string prefix = "Operation ";
        if (!fieldName.StartsWith(prefix, StringComparison.Ordinal))
            return int.MaxValue;

        var separator = fieldName.IndexOf(':', prefix.Length);
        return separator > prefix.Length &&
               int.TryParse(fieldName.AsSpan(prefix.Length, separator - prefix.Length), out var ordinal)
            ? ordinal
            : int.MaxValue;
    }

    private static ConfidenceBreakdownDto NotReported() => new(
        Overall: null,
        Components: Array.Empty<ConfidenceComponentDto>(),
        Note: "No trustworthy confidence value was reported for this proposal.",
        Threshold: null,
        Source: ConfidenceBreakdownDto.NotReportedSource);
}
