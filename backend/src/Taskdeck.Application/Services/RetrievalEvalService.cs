using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

/// <summary>
/// Measures retrieval quality against a labeled holdout set.
/// Computes recall@K and precision@K for a given retrieval configuration.
/// </summary>
public static class RetrievalEvalService
{
    /// <summary>
    /// A single labeled evaluation case: a query with known relevant document IDs.
    /// </summary>
    public sealed record EvalCase(
        string Query,
        IReadOnlySet<Guid> RelevantDocumentIds,
        string? Description = null);

    /// <summary>
    /// Results of evaluating a retrieval system against a holdout set.
    /// </summary>
    public sealed record EvalReport(
        int TotalCases,
        double MeanRecallAtK,
        double MeanPrecisionAtK,
        int K,
        IReadOnlyList<CaseResult> CaseResults);

    /// <summary>
    /// Per-case evaluation result.
    /// </summary>
    public sealed record CaseResult(
        string Query,
        double RecallAtK,
        double PrecisionAtK,
        int RetrievedCount,
        int RelevantRetrievedCount,
        int TotalRelevant);

    /// <summary>
    /// Evaluates retrieval quality by running each eval case through the
    /// retrieval service and measuring recall@K and precision@K.
    /// </summary>
    /// <param name="retrievalService">The retrieval service to evaluate.</param>
    /// <param name="evalCases">Labeled holdout cases.</param>
    /// <param name="userId">User context for the queries.</param>
    /// <param name="k">Number of results to evaluate at.</param>
    /// <param name="boardId">Optional board scope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<EvalReport> EvaluateAsync(
        IHybridRetrievalService retrievalService,
        IReadOnlyList<EvalCase> evalCases,
        Guid userId,
        int k = 10,
        Guid? boardId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retrievalService);
        ArgumentNullException.ThrowIfNull(evalCases);

        if (k <= 0)
            throw new ArgumentOutOfRangeException(nameof(k), "K must be positive");

        if (evalCases.Count == 0)
            return new EvalReport(
                TotalCases: 0,
                MeanRecallAtK: 0.0,
                MeanPrecisionAtK: 0.0,
                K: k,
                CaseResults: Array.Empty<CaseResult>());

        var caseResults = new List<CaseResult>(evalCases.Count);
        double totalRecall = 0;
        double totalPrecision = 0;

        foreach (var evalCase in evalCases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var results = await retrievalService.SearchAsync(
                evalCase.Query,
                userId,
                boardId,
                limit: k,
                cancellationToken: cancellationToken);

            var retrievedIds = results.Select(r => r.DocumentId).ToHashSet();
            var relevantRetrieved = retrievedIds.Intersect(evalCase.RelevantDocumentIds).Count();

            var recallAtK = evalCase.RelevantDocumentIds.Count > 0
                ? (double)relevantRetrieved / evalCase.RelevantDocumentIds.Count
                : 1.0; // If no relevant docs exist, recall is trivially 1.0

            var precisionAtK = results.Count > 0
                ? (double)relevantRetrieved / results.Count
                : 0.0;

            totalRecall += recallAtK;
            totalPrecision += precisionAtK;

            caseResults.Add(new CaseResult(
                Query: evalCase.Query,
                RecallAtK: recallAtK,
                PrecisionAtK: precisionAtK,
                RetrievedCount: results.Count,
                RelevantRetrievedCount: relevantRetrieved,
                TotalRelevant: evalCase.RelevantDocumentIds.Count));
        }

        return new EvalReport(
            TotalCases: evalCases.Count,
            MeanRecallAtK: totalRecall / evalCases.Count,
            MeanPrecisionAtK: totalPrecision / evalCases.Count,
            K: k,
            CaseResults: caseResults);
    }

    /// <summary>
    /// Evaluates duplicate detection precision: measures the fraction of
    /// flagged duplicates that are true positives.
    /// </summary>
    public sealed record DuplicateEvalCase(
        string Content,
        string Title,
        bool IsActualDuplicate,
        string? Description = null);

    public sealed record DuplicateEvalReport(
        int TotalCases,
        double Precision,
        double Recall,
        int TruePositives,
        int FalsePositives,
        int FalseNegatives,
        int TrueNegatives);

    public static async Task<DuplicateEvalReport> EvaluateDuplicateDetectionAsync(
        IDuplicateDetectionService detectionService,
        IReadOnlyList<DuplicateEvalCase> evalCases,
        Guid userId,
        Guid? boardId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detectionService);
        ArgumentNullException.ThrowIfNull(evalCases);

        if (evalCases.Count == 0)
            return new DuplicateEvalReport(0, 0, 0, 0, 0, 0, 0);

        int tp = 0, fp = 0, fn = 0, tn = 0;

        foreach (var evalCase in evalCases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await detectionService.DetectAsync(
                evalCase.Content,
                evalCase.Title,
                userId,
                boardId,
                cancellationToken: cancellationToken);

            if (result.IsProbableDuplicate && evalCase.IsActualDuplicate)
                tp++;
            else if (result.IsProbableDuplicate && !evalCase.IsActualDuplicate)
                fp++;
            else if (!result.IsProbableDuplicate && evalCase.IsActualDuplicate)
                fn++;
            else
                tn++;
        }

        var precision = (tp + fp) > 0 ? (double)tp / (tp + fp) : 0.0;
        var recall = (tp + fn) > 0 ? (double)tp / (tp + fn) : 0.0;

        return new DuplicateEvalReport(
            TotalCases: evalCases.Count,
            Precision: precision,
            Recall: recall,
            TruePositives: tp,
            FalsePositives: fp,
            FalseNegatives: fn,
            TrueNegatives: tn);
    }
}
