using System.Security.Cryptography;
using System.Text;

namespace Taskdeck.AccelerationCandidates;

public enum CandidateReviewState { Pending = 0, Accepted = 1, Rejected = 2, Superseded = 3 }

public sealed class SemanticCandidate
{
    private SemanticCandidate() { }

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid ProcessingRunId { get; private set; }
    public string CandidateType { get; private set; } = string.Empty;
    public string ContractVersion { get; private set; } = string.Empty;
    public string SemanticKey { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public double? Confidence { get; private set; }
    public CandidateReviewState ReviewState { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public Guid? ReviewedByPrincipalId { get; private set; }

    public static SemanticCandidate Create(
        Guid ownerUserId,
        Guid processingRunId,
        string candidateType,
        string contractVersion,
        ReadOnlySpan<byte> canonicalPayload,
        IEnumerable<Guid> evidenceAnchorIds,
        double? confidence,
        DateTimeOffset now)
    {
        if (ownerUserId == Guid.Empty || processingRunId == Guid.Empty)
            throw new ArgumentException("Owner and processing run are required.");
        if (string.IsNullOrWhiteSpace(candidateType) || string.IsNullOrWhiteSpace(contractVersion))
            throw new ArgumentException("Candidate type and contract version are required.");
        if (confidence is < 0 or > 1 || double.IsNaN(confidence ?? 0) || double.IsInfinity(confidence ?? 0))
            throw new ArgumentOutOfRangeException(nameof(confidence));

        var anchors = evidenceAnchorIds.Distinct().Order().ToArray();
        if (anchors.Length == 0 || anchors.Any(x => x == Guid.Empty))
            throw new ArgumentException("At least one valid evidence anchor is required.");

        var payloadHash = Convert.ToHexString(SHA256.HashData(canonicalPayload)).ToLowerInvariant();
        var semanticMaterial = $"{ownerUserId:N}|{processingRunId:N}|{candidateType.Trim()}|{contractVersion.Trim()}|{payloadHash}|{string.Join(',', anchors.Select(x => x.ToString("N")))}";
        var semanticKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semanticMaterial))).ToLowerInvariant();

        return new SemanticCandidate
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId, ProcessingRunId = processingRunId,
            CandidateType = candidateType.Trim(), ContractVersion = contractVersion.Trim(),
            SemanticKey = semanticKey, PayloadHash = payloadHash, Confidence = confidence,
            ReviewState = CandidateReviewState.Pending, CreatedAt = now,
        };
    }

    public void Review(bool accept, Guid principalId, DateTimeOffset now)
    {
        if (ReviewState != CandidateReviewState.Pending)
            throw new InvalidOperationException("Only pending candidates can be reviewed.");
        if (principalId == Guid.Empty) throw new ArgumentException("Principal is required.");
        ReviewState = accept ? CandidateReviewState.Accepted : CandidateReviewState.Rejected;
        ReviewedByPrincipalId = principalId; ReviewedAt = now;
    }
}
