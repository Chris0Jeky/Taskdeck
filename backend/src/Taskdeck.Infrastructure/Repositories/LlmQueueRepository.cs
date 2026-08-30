using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// SCAFFOLDING: Placeholder repository implementation for LlmRequest entity.
/// </summary>
public class LlmQueueRepository : Repository<LlmRequest>, ILlmQueueRepository
{
    private const string CaptureRequestTypeLike = "inbox.capture.%";
    private const int EffectiveBoardLookupBatchSize = 500;
    private const string ProvenanceBoardIdMarker = "\"boardId\":\"";
    private const string ProvenanceProposalIdMarker = "\"proposalId\":\"";

    // Transcript captures nest under the capture prefix (so user-facing capture queries keep
    // matching them) but form their own worker lane: LLM-backed triage runs seconds-to-minutes and
    // must not block the millisecond-latency capture lane (REVIVAL-08). Worker-lane predicates
    // (oldest-Processing fetch, Processing counts, Processing claims) treat capture and transcript
    // as DISJOINT kinds; user-facing queries (GetCaptureSummaryByUserAsync, GetCapturesByUserAsync,
    // CountPendingCaptureAsync) keep the inclusive capture prefix.
    private const string TranscriptRequestTypeLike = "inbox.capture.transcript.%";

    /// <summary>Worker-lane request kinds. See <see cref="TranscriptRequestTypeLike"/>.</summary>
    private enum QueueLane
    {
        NonCapture,
        Capture,
        Transcript,
    }

    public LlmQueueRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.LlmRequests
                .FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)RequestStatus.Pending} ORDER BY CreatedAt ASC LIMIT {limit}")
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == RequestStatus.Pending)
            .OrderBy(lr => lr.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<(int TotalCaptures, int NewCount, int FailedCount, int TriagingCount, int TriagedCount)> GetCaptureSummaryByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var captureQuery = _context.LlmRequests
            .AsNoTracking()
            .Where(request =>
                request.UserId == userId
                && EF.Functions.Like(request.RequestType, CaptureRequestTypeLike));

        var statusCounts = await captureQuery
            .GroupBy(request => request.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        var countsByStatus = statusCounts.ToDictionary(group => group.Status, group => group.Count);
        var completedWithLinkedProposal = await CountCompletedCapturesWithProposalAsync(
            userId,
            cancellationToken);
        var archivedCaptures = await GetArchivedCaptureSummaryCandidatesAsync(
            captureQuery,
            cancellationToken);
        var keptStatusCounts = await captureQuery
            .Where(request =>
                (request.Status == RequestStatus.Pending || request.Status == RequestStatus.Failed) &&
                (request.Payload.Contains("\"kind\":\"kept\"") || request.Payload.Contains("\"kind\":0")))
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Status, group => group.Count, cancellationToken);
        var archivedCountsByStatus = archivedCaptures
            .GroupBy(capture => capture.Status)
            .ToDictionary(group => group.Key, group => group.Count());

        countsByStatus.TryGetValue(RequestStatus.Completed, out var completedCount);
        countsByStatus.TryGetValue(RequestStatus.Pending, out var pendingCount);
        countsByStatus.TryGetValue(RequestStatus.Failed, out var failedCount);
        countsByStatus.TryGetValue(RequestStatus.Processing, out var processingCount);
        archivedCountsByStatus.TryGetValue(RequestStatus.Completed, out var archivedCompletedCount);
        archivedCountsByStatus.TryGetValue(RequestStatus.Pending, out var archivedPendingCount);
        archivedCountsByStatus.TryGetValue(RequestStatus.Failed, out var archivedFailedCount);
        archivedCountsByStatus.TryGetValue(RequestStatus.Processing, out var archivedProcessingCount);
        keptStatusCounts.TryGetValue(RequestStatus.Pending, out var keptPendingCount);
        keptStatusCounts.TryGetValue(RequestStatus.Failed, out var keptFailedCount);
        var archivedKeptPendingCount = archivedCaptures.Count(capture =>
            capture.Status == RequestStatus.Pending && capture.IsKept);
        var archivedKeptFailedCount = archivedCaptures.Count(capture =>
            capture.Status == RequestStatus.Failed && capture.IsKept);
        var archivedCompletedWithLinkedProposal = archivedCaptures.Count(capture =>
            capture.Status == RequestStatus.Completed && capture.HasLinkedProposal);
        var activeCompletedCount = Math.Max(0, completedCount - archivedCompletedCount);
        var activeCompletedWithLinkedProposal = Math.Max(
            0,
            completedWithLinkedProposal - archivedCompletedWithLinkedProposal);

        return (
            TotalCaptures: countsByStatus.Values.Sum(),
            NewCount: Math.Max(0, pendingCount - archivedPendingCount - (keptPendingCount - archivedKeptPendingCount)),
            FailedCount: Math.Max(0, failedCount - archivedFailedCount - (keptFailedCount - archivedKeptFailedCount)),
            TriagingCount: Math.Max(0, processingCount - archivedProcessingCount),
            TriagedCount: Math.Max(0, activeCompletedCount - activeCompletedWithLinkedProposal));
    }

    private async Task<int> CountCompletedCapturesWithProposalAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.Database
                .SqlQuery<int>(
                    $"""
                    SELECT COUNT(*) AS Value
                    FROM LlmRequests
                    WHERE UserId = {userId}
                      AND RequestType LIKE {CaptureRequestTypeLike}
                      AND Status = {(int)RequestStatus.Completed}
                      AND json_extract(Payload, '$.provenance.proposalId') IS NOT NULL
                    """)
                .SingleAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .AsNoTracking()
            .Where(request =>
                request.UserId == userId
                && request.Status == RequestStatus.Completed
                && EF.Functions.Like(request.RequestType, CaptureRequestTypeLike)
                && request.Payload.Contains("\"proposalId\":\""))
            .CountAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ArchivedCaptureSummaryCandidate>> GetArchivedCaptureSummaryCandidatesAsync(
        IQueryable<LlmRequest> captureQuery,
        CancellationToken cancellationToken)
    {
        // Raw BoardId is authoritative. Only direct archived rows and null-board rows carrying a
        // server-serialized board/proposal marker need payload resolution; ordinary boardless rows
        // remain active without materializing potentially large transcript payloads.
        var candidates = await captureQuery
            .Where(request =>
                (request.BoardId.HasValue && _context.Boards.Any(board =>
                    board.Id == request.BoardId.Value && board.IsArchived)) ||
                (!request.BoardId.HasValue &&
                    (request.Payload.Contains(ProvenanceBoardIdMarker) ||
                     request.Payload.Contains(ProvenanceProposalIdMarker))))
            .Select(request => new CaptureSummaryCandidate(
                request.Id,
                request.UserId,
                request.BoardId,
                request.Payload,
                request.Status))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return Array.Empty<ArchivedCaptureSummaryCandidate>();
        }

        var parsedCandidates = candidates
            .Select(candidate => new ParsedCaptureSummaryCandidate(
                candidate,
                CaptureRequestContract.ParseStoredPayload(candidate.Payload)))
            .ToList();
        var proposalIds = parsedCandidates
            .Select(candidate => candidate.Payload.Provenance?.ProposalId)
            .Where(proposalId => proposalId.HasValue && proposalId.Value != Guid.Empty)
            .Select(proposalId => proposalId!.Value)
            .Distinct()
            .ToArray();
        var proposalsById = new Dictionary<Guid, AutomationProposal>();
        foreach (var proposalIdBatch in proposalIds.Chunk(EffectiveBoardLookupBatchSize))
        {
            var proposals = await _context.AutomationProposals
                .AsNoTracking()
                .Where(proposal => proposalIdBatch.Contains(proposal.Id))
                .ToListAsync(cancellationToken);
            foreach (var proposal in proposals)
            {
                proposalsById[proposal.Id] = proposal;
            }
        }

        var resolvedCandidates = parsedCandidates
            .Select(candidate =>
            {
                AutomationProposal? proposal = null;
                var proposalId = candidate.Payload.Provenance?.ProposalId;
                if (proposalId.HasValue && proposalId.Value != Guid.Empty)
                {
                    proposalsById.TryGetValue(proposalId.Value, out proposal);
                }

                return new ResolvedCaptureSummaryCandidate(
                    candidate.Candidate,
                    candidate.Payload.Disposition?.Kind == CaptureDisposition.Kept,
                    CaptureEffectiveBoardPolicy.ResolveEffectiveBoardId(
                        candidate.Candidate.Id,
                        candidate.Candidate.UserId,
                        candidate.Candidate.BoardId,
                        candidate.Payload.Provenance?.BoardId,
                        proposalId,
                        candidate.Payload.Provenance?.ConvertedAt,
                        proposal));
            })
            .ToList();
        var effectiveBoardIds = resolvedCandidates
            .Where(candidate => candidate.EffectiveBoardId.HasValue)
            .Select(candidate => candidate.EffectiveBoardId!.Value)
            .Distinct()
            .ToArray();
        var archivedBoardIds = new HashSet<Guid>();
        foreach (var boardIdBatch in effectiveBoardIds.Chunk(EffectiveBoardLookupBatchSize))
        {
            var batchArchivedBoardIds = await _context.Boards
                .AsNoTracking()
                .Where(board => boardIdBatch.Contains(board.Id) && board.IsArchived)
                .Select(board => board.Id)
                .ToListAsync(cancellationToken);
            archivedBoardIds.UnionWith(batchArchivedBoardIds);
        }

        return resolvedCandidates
            .Where(candidate =>
                candidate.EffectiveBoardId.HasValue &&
                archivedBoardIds.Contains(candidate.EffectiveBoardId.Value))
            .Select(candidate => new ArchivedCaptureSummaryCandidate(
                candidate.Candidate.Status,
                candidate.IsKept,
                candidate.Candidate.Payload.Contains(
                    ProvenanceProposalIdMarker,
                    StringComparison.Ordinal)))
            .ToList();
    }

    private sealed record CaptureSummaryCandidate(
        Guid Id,
        Guid UserId,
        Guid? BoardId,
        string Payload,
        RequestStatus Status);

    private sealed record ParsedCaptureSummaryCandidate(
        CaptureSummaryCandidate Candidate,
        CapturePayloadV1 Payload);

    private sealed record ResolvedCaptureSummaryCandidate(
        CaptureSummaryCandidate Candidate,
        bool IsKept,
        Guid? EffectiveBoardId);

    private sealed record ArchivedCaptureSummaryCandidate(
        RequestStatus Status,
        bool IsKept,
        bool HasLinkedProposal);

    public async Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            // FromSqlInterpolated + Include may not preserve ORDER BY from the raw SQL
            // (EF Core wraps it in a subquery), so sort in-memory after materialization.
            var results = await _context.LlmRequests
                .FromSqlInterpolated($"SELECT * FROM LlmRequests WHERE UserId = {userId}")
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
            return results.OrderByDescending(lr => lr.CreatedAt).ToList();
        }

        return await _context.LlmRequests
            .Include(lr => lr.Board)
            .Where(lr => lr.UserId == userId)
            .OrderByDescending(lr => lr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<LlmRequest>> GetCapturesByUserAsync(Guid userId, int limit, int offset, Guid? boardId = null, CancellationToken cancellationToken = default)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be at least 1.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset cannot be negative.");
        }

        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate ORDER BY on a DateTimeOffset column from LINQ, so the ordering +
            // LIMIT/OFFSET live in raw SQL. The (CreatedAt desc, Id) total order keeps paging stable so no
            // row is skipped or duplicated across pages. The re-sort defensively re-establishes that order
            // in case EF reshapes the query; the tie-break uses Id.ToString() with an ordinal comparison so
            // it matches SQLite's TEXT comparison of the stored Guid exactly (the raw SQL's `ORDER BY Id`),
            // rather than Guid.CompareTo, which orders by a different key. No Include: the only caller
            // (CaptureService.ListAsync) reads the scalar BoardId, never the Board navigation.
            // #1239: when a board filter is supplied, keep captures whose raw BoardId matches OR is
            // NULL (null-board captures may still resolve to the target board via provenance, resolved
            // in the service). This excludes other boards' captures from the scan at the database. The
            // interpolation holes are parameterized by FromSqlInterpolated (no SQL injection).
            // if/else (not a ternary): each branch must be target-typed to FormattableString so the
            // interpolation holes stay parameters; a ternary would infer string and lose parameterization.
            FormattableString sql;
            if (boardId.HasValue)
            {
                sql = $"SELECT * FROM LlmRequests WHERE UserId = {userId} AND RequestType LIKE {CaptureRequestTypeLike} AND (BoardId IS NULL OR BoardId = {boardId.Value}) ORDER BY CreatedAt DESC, Id LIMIT {limit} OFFSET {offset}";
            }
            else
            {
                sql = $"SELECT * FROM LlmRequests WHERE UserId = {userId} AND RequestType LIKE {CaptureRequestTypeLike} ORDER BY CreatedAt DESC, Id LIMIT {limit} OFFSET {offset}";
            }
            var rows = await _context.LlmRequests
                .FromSqlInterpolated(sql)
                .ToListAsync(cancellationToken);
            return rows
                .OrderByDescending(lr => lr.CreatedAt)
                .ThenBy(lr => lr.Id.ToString(), StringComparer.Ordinal)
                .ToList();
        }

        // Non-SQLite providers (e.g. the Postgres Testcontainer path) order in the database. EF.Functions.Like
        // mirrors the capture predicate used across this repository (e.g. GetCaptureSummaryByUserAsync); on a
        // case-sensitive provider it is stricter than IsCaptureRequestType, but production is SQLite-only.
        var query = _context.LlmRequests
            .Where(lr => lr.UserId == userId && EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike));
        if (boardId.HasValue)
        {
            // Same raw-board pre-filter as the SQLite path (#1239): match the board or keep null-board rows.
            query = query.Where(lr => lr.BoardId == null || lr.BoardId == boardId.Value);
        }
        return await query
            .OrderByDescending(lr => lr.CreatedAt)
            .ThenBy(lr => lr.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<IEnumerable<LlmRequest>> GetByStatusAsync(RequestStatus status, CancellationToken cancellationToken = default)
        => GetByStatusCoreAsync(status, limit: null, cancellationToken);

    public Task<IEnumerable<LlmRequest>> GetByStatusForDisplayAsync(RequestStatus status, int limit, CancellationToken cancellationToken = default)
    {
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be at least 1.");
        return GetByStatusCoreAsync(status, limit, cancellationToken);
    }

    private async Task<IEnumerable<LlmRequest>> GetByStatusCoreAsync(RequestStatus status, int? limit, CancellationToken cancellationToken)
    {
        if (_context.Database.IsSqlite())
        {
            // SQLite's EF provider can't ORDER BY a DateTimeOffset column in LINQ, so the order
            // (and the optional display bound) live in raw SQL.
            FormattableString sql;
            if (limit is int n)
                sql = $"SELECT * FROM LlmRequests WHERE Status = {(int)status} ORDER BY CreatedAt DESC LIMIT {n}";
            else
                sql = $"SELECT * FROM LlmRequests WHERE Status = {(int)status} ORDER BY CreatedAt DESC";

            var rows = await _context.LlmRequests
                .FromSqlInterpolated(sql)
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
            // FromSqlInterpolated + Include wraps the raw SQL in a subquery whose outer ORDER BY is
            // not guaranteed to survive to the final result; re-sort newest-first in memory so the
            // documented contract holds (same workaround as GetOldestByStatusAndCaptureKindAsync).
            return rows.OrderByDescending(lr => lr.CreatedAt).ToList();
        }

        var query = _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == status)
            .OrderByDescending(lr => lr.CreatedAt);

        return limit is int take
            ? await query.Take(take).ToListAsync(cancellationToken)
            : await query.ToListAsync(cancellationToken);
    }

    public Task<IEnumerable<LlmRequest>> GetOldestPendingNonCaptureAsync(int limit, CancellationToken cancellationToken = default)
        => GetOldestByStatusAndLaneAsync(RequestStatus.Pending, QueueLane.NonCapture, limit, cancellationToken);

    public Task<IEnumerable<LlmRequest>> GetOldestProcessingCaptureAsync(int limit, CancellationToken cancellationToken = default)
        => GetOldestByStatusAndLaneAsync(RequestStatus.Processing, QueueLane.Capture, limit, cancellationToken);

    public Task<IEnumerable<LlmRequest>> GetOldestProcessingTranscriptAsync(int limit, CancellationToken cancellationToken = default)
        => GetOldestByStatusAndLaneAsync(RequestStatus.Processing, QueueLane.Transcript, limit, cancellationToken);

    private async Task<IEnumerable<LlmRequest>> GetOldestByStatusAndLaneAsync(
        RequestStatus status,
        QueueLane lane,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be at least 1.");
        }

        if (_context.Database.IsSqlite())
        {
            // SQLite's EF provider cannot translate ORDER BY on a DateTimeOffset column, so the order +
            // LIMIT live in raw SQL. FromSqlInterpolated + Include wraps this in a subquery that does not
            // guarantee the raw ORDER BY survives to the final result (see GetByUserAsync); the inner LIMIT
            // still selects the correct oldest-N rows, so re-sort in memory to make oldest-first a contract.
            var rawQuery = lane switch
            {
                QueueLane.Capture => _context.LlmRequests.FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)status} AND RequestType LIKE {CaptureRequestTypeLike} AND RequestType NOT LIKE {TranscriptRequestTypeLike} ORDER BY CreatedAt ASC LIMIT {limit}"),
                QueueLane.Transcript => _context.LlmRequests.FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)status} AND RequestType LIKE {TranscriptRequestTypeLike} ORDER BY CreatedAt ASC LIMIT {limit}"),
                _ => _context.LlmRequests.FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)status} AND RequestType NOT LIKE {CaptureRequestTypeLike} ORDER BY CreatedAt ASC LIMIT {limit}"),
            };

            var rows = await rawQuery
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
            return rows.OrderBy(lr => lr.CreatedAt).ToList();
        }

        // Non-SQLite providers (e.g. the Postgres Testcontainer path) translate the predicate + OrderBy +
        // Take into a single bounded query with guaranteed ordering.
        var query = _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == status);

        query = ApplyLanePredicate(query, lane);

        return await query
            .OrderBy(lr => lr.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<LlmRequest> ApplyLanePredicate(IQueryable<LlmRequest> query, QueueLane lane)
    {
        return lane switch
        {
            QueueLane.Capture => query.Where(lr =>
                EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike) &&
                !EF.Functions.Like(lr.RequestType, TranscriptRequestTypeLike)),
            QueueLane.Transcript => query.Where(lr =>
                EF.Functions.Like(lr.RequestType, TranscriptRequestTypeLike)),
            _ => query.Where(lr => !EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike)),
        };
    }

    public Task<int> CountPendingNonCaptureAsync(CancellationToken cancellationToken = default)
        => CountByStatusAndLaneAsync(RequestStatus.Pending, QueueLane.NonCapture, cancellationToken);

    public Task<int> CountProcessingCaptureAsync(CancellationToken cancellationToken = default)
        => CountByStatusAndLaneAsync(RequestStatus.Processing, QueueLane.Capture, cancellationToken);

    public Task<int> CountProcessingTranscriptAsync(CancellationToken cancellationToken = default)
        => CountByStatusAndLaneAsync(RequestStatus.Processing, QueueLane.Transcript, cancellationToken);

    public async Task<int> CountPendingCaptureAsync(CancellationToken cancellationToken = default)
    {
        // Deliberately the inclusive capture prefix, NOT the Capture lane predicate: Pending means
        // "in the inbox, triage not requested yet" — an inbox-depth gauge where transcript captures
        // count like any other capture. The lane split only governs Processing-row ownership.
        return await _context.LlmRequests
            .AsNoTracking()
            .Where(lr => lr.Status == RequestStatus.Pending
                && EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike))
            .CountAsync(cancellationToken);
    }

    private async Task<int> CountByStatusAndLaneAsync(
        RequestStatus status,
        QueueLane lane,
        CancellationToken cancellationToken)
    {
        var query = _context.LlmRequests
            .AsNoTracking()
            .Where(lr => lr.Status == status);

        query = ApplyLanePredicate(query, lane);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LlmRequest>> GetStuckProcessingNonCaptureAsync(
        DateTimeOffset staleBefore,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be at least 1.");
        }

        if (_context.Database.IsSqlite())
        {
            // SQLite's EF provider cannot translate WHERE/ORDER BY on a DateTimeOffset column, so the
            // staleness comparison + order + LIMIT live in raw SQL. The TEXT comparison is chronological
            // because every UpdatedAt writer (Entity ctor/Touch and the raw claim UPDATEs) and staleBefore
            // are all DateTimeOffset.UtcNow, so they share a fixed-width "+00:00" offset and lexical order
            // equals chronological order -- the same shape the shipped
            // OutboundWebhookDeliveryRepository.GetStuckProcessingAsync relies on.
            // FromSqlInterpolated + Include wraps this in a subquery that does not guarantee the raw
            // ORDER BY survives (see GetByUserAsync); the inner LIMIT still selects the correct oldest-N
            // rows, so re-sort oldest-first in memory to make the ordering a contract.
            // No Include: the recovery sweep only mutates scalar fields (Status/ErrorMessage/RetryCount/
            // UpdatedAt) and never reads the User/Board navigations, so loading them is wasted work.
            var rows = await _context.LlmRequests
                .FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)RequestStatus.Processing} AND RequestType NOT LIKE {CaptureRequestTypeLike} AND UpdatedAt <= {staleBefore} ORDER BY UpdatedAt ASC LIMIT {limit}")
                .ToListAsync(cancellationToken);
            return rows.OrderBy(lr => lr.UpdatedAt).ToList();
        }

        // Non-SQLite providers (e.g. the Postgres Testcontainer path) translate the predicate + OrderBy +
        // Take into a single bounded query with guaranteed ordering. No Include (see SQLite path above).
        return await _context.LlmRequests
            .Where(lr => lr.Status == RequestStatus.Processing
                && !EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike)
                && lr.UpdatedAt <= staleBefore)
            .OrderBy(lr => lr.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<LlmRequest>> GetByUserAndStatusAsync(Guid userId, RequestStatus status, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.LlmRequests
                .FromSqlInterpolated($"SELECT * FROM LlmRequests WHERE UserId = {userId} AND Status = {(int)status} ORDER BY CreatedAt DESC")
                .AsNoTracking()
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .AsNoTracking()
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.UserId == userId && lr.Status == status)
            .OrderByDescending(lr => lr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<RequestStatus, int>> GetStatusCountsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.LlmRequests
            .Where(r => r.UserId == userId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
    }

    public async Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.LlmRequests
                .FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)RequestStatus.Pending} ORDER BY CreatedAt ASC LIMIT 1")
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == RequestStatus.Pending)
            .OrderBy(lr => lr.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryClaimProcessingCaptureAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        var claimedAt = DateTimeOffset.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE LlmRequests
            SET UpdatedAt = {claimedAt}
            WHERE Id = {requestId}
              AND Status = {(int)RequestStatus.Processing}
              AND UpdatedAt = {expectedUpdatedAt}
              AND RequestType LIKE {CaptureRequestTypeLike}
              AND RequestType NOT LIKE {TranscriptRequestTypeLike}
            """,
            cancellationToken);

        if (rowsAffected == 0)
        {
            return false;
        }

        // The raw-SQL UPDATE bypasses the EF change tracker. If this context already
        // tracks the entity (e.g. it was materialized by GetOldestProcessingCaptureAsync),
        // reload it so callers holding the instance observe the persisted claim timestamp.
        var tracked = _context.LlmRequests.Local.FirstOrDefault(lr => lr.Id == requestId);
        if (tracked != null)
        {
            await _context.Entry(tracked).ReloadAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> TrySetCaptureDispositionAsync(
        Guid requestId,
        RequestStatus expectedStatus,
        DateTimeOffset expectedUpdatedAt,
        RequestStatus targetStatus,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE LlmRequests
            SET Status = {(int)targetStatus}, Payload = {payload}, UpdatedAt = {updatedAt}
            WHERE Id = {requestId}
              AND Status = {(int)expectedStatus}
              AND UpdatedAt = {expectedUpdatedAt}
              AND RequestType LIKE {CaptureRequestTypeLike}
            """,
            cancellationToken);

        await ReloadTrackedRequestAsync(requestId, cancellationToken);
        return rowsAffected > 0;
    }

    public async Task<bool> TryEnqueueCaptureTriageAsync(
        Guid requestId,
        RequestStatus expectedStatus,
        DateTimeOffset expectedUpdatedAt,
        string payload,
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE LlmRequests
            SET Status = {(int)RequestStatus.Processing}, Payload = {payload}, BoardId = {boardId},
                ErrorMessage = NULL, ProcessedAt = NULL, UpdatedAt = {updatedAt}
            WHERE Id = {requestId}
              AND Status = {(int)expectedStatus}
              AND UpdatedAt = {expectedUpdatedAt}
              AND RequestType LIKE {CaptureRequestTypeLike}
            """,
            cancellationToken);

        await ReloadTrackedRequestAsync(requestId, cancellationToken);
        return rowsAffected > 0;
    }

    private async Task ReloadTrackedRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var tracked = _context.LlmRequests.Local.FirstOrDefault(request => request.Id == requestId);
        if (tracked != null)
        {
            await _context.Entry(tracked).ReloadAsync(cancellationToken);
        }
    }

    public async Task<bool> TryClaimProcessingTranscriptAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        // Same shape as the capture claim: transcript items also live in Processing while queued
        // for triage (the API's triage endpoint marks them Processing), so the claim is a pure
        // UpdatedAt stamp under optimistic concurrency. The lane predicate makes the capture and
        // transcript claims mutually exclusive so the two workers can never claim each other's rows.
        var claimedAt = DateTimeOffset.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE LlmRequests
            SET UpdatedAt = {claimedAt}
            WHERE Id = {requestId}
              AND Status = {(int)RequestStatus.Processing}
              AND UpdatedAt = {expectedUpdatedAt}
              AND RequestType LIKE {TranscriptRequestTypeLike}
            """,
            cancellationToken);

        return rowsAffected > 0;
    }

    public async Task<bool> TryClaimProcessingAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        var claimedAt = DateTimeOffset.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE LlmRequests
            SET Status = {(int)RequestStatus.Processing}, UpdatedAt = {claimedAt}
            WHERE Id = {requestId}
              AND Status = {(int)RequestStatus.Pending}
              AND UpdatedAt = {expectedUpdatedAt}
              AND RequestType NOT LIKE {CaptureRequestTypeLike}
            """,
            cancellationToken);

        if (rowsAffected == 0)
        {
            return false;
        }

        // The raw-SQL UPDATE bypasses the EF change tracker. If this context already
        // tracks the entity (e.g. it was materialized by GetByStatusAsync), reload it so
        // callers holding the instance -- and GetByIdAsync, whose FindAsync serves the
        // identity map -- observe the claimed Processing state instead of stale Pending.
        var tracked = _context.LlmRequests.Local.FirstOrDefault(lr => lr.Id == requestId);
        if (tracked != null)
        {
            await _context.Entry(tracked).ReloadAsync(cancellationToken);
        }

        return true;
    }
}
