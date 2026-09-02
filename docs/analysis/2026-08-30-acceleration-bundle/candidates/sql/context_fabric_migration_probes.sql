-- Context Fabric migration probes (SQLite candidate)
-- Confirm exact table/column names against the current EF migration before running.
-- Every query should return zero rows unless the comment states otherwise.

-- 1. Legacy capture-shaped queue rows missing an ID-preserving Capture.
SELECT l.Id, l.RequestType
FROM LlmRequests AS l
LEFT JOIN Captures AS c ON c.Id = l.Id
WHERE l.RequestType LIKE 'inbox.capture.%'
  AND c.Id IS NULL;

-- 2. Native Capture IDs that do not have a legacy source during the migration window.
-- This is informational after native-only intake starts; record count by intake version.
SELECT COUNT(*) AS NativeOnlyCaptureCount
FROM Captures AS c
LEFT JOIN LlmRequests AS l ON l.Id = c.Id
WHERE l.Id IS NULL;

-- 3. Duplicate source assets or captures cannot exist by primary key; this checks
-- one inline source asset was created for each text-bearing legacy capture.
SELECT c.Id, COUNT(sa.Id) AS InlineAssetCount
FROM Captures AS c
JOIN LlmRequests AS l ON l.Id = c.Id
LEFT JOIN SourceAssets AS sa
  ON sa.CaptureId = c.Id AND sa.StorageKind = 'InlineText'
WHERE l.RequestType LIKE 'inbox.capture.%'
GROUP BY c.Id
HAVING COUNT(sa.Id) = 0;

-- 4. Capture state axes must remain independently valid.
SELECT Id, Disposition, ProcessingSummary, ActionState
FROM Captures
WHERE Disposition NOT IN ('Unreviewed','Kept','Archived','Dismissed')
   OR ProcessingSummary NOT IN ('Idle','Processing','Partial','Ready','Failed')
   OR ActionState NOT IN ('None','ProposalRequested','ProposalReady','Applied','Blocked');

-- 5. Auto may be requested, never effective.
SELECT Id, RequestedIntent, EffectiveIntent, IntentResolvedByRunId
FROM Captures
WHERE EffectiveIntent = 'Auto'
   OR (EffectiveIntent IS NOT NULL AND IntentResolvedByRunId IS NULL);

-- 6. Representation parent XOR and ownership/capture completion.
SELECT Id, ParentSourceAssetId, ParentRepresentationId, UserId, CaptureId
FROM Representations
WHERE (ParentSourceAssetId IS NULL) = (ParentRepresentationId IS NULL)
   OR UserId IS NULL
   OR CaptureId IS NULL;

-- 7. Forward supersession must not point to self.
SELECT Id, SupersededByRepresentationId
FROM Representations
WHERE SupersededByRepresentationId = Id;

-- 8. Evidence anchors must resolve to an owned representation.
SELECT a.Id, a.RepresentationId
FROM EvidenceAnchors AS a
LEFT JOIN Representations AS r ON r.Id = a.RepresentationId
WHERE r.Id IS NULL OR r.UserId <> a.UserId;

-- 9. Processing terminal jobs should have a run receipt.
SELECT j.Id, j.State
FROM ProcessingJobs AS j
LEFT JOIN ProcessingRuns AS r ON r.JobId = j.Id
WHERE j.State IN ('Succeeded','Failed')
GROUP BY j.Id, j.State
HAVING COUNT(r.Id) = 0;

-- 10. Valid active leases require token/owner/expiry together.
SELECT Id, State, LeaseToken, LeaseOwner, LeaseExpiresAt
FROM ProcessingJobs
WHERE State IN ('Leased','Running')
  AND (LeaseToken IS NULL OR LeaseOwner IS NULL OR LeaseExpiresAt IS NULL);

-- 11. Blob references must stay inside the object's owner boundary.
SELECT br.Id, br.OwnerUserId, bo.OwnerUserId
FROM BlobReferences AS br
JOIN BlobObjects AS bo ON bo.Id = br.BlobObjectId
WHERE br.OwnerUserId <> bo.OwnerUserId;

-- 12. Unreferenced BlobObjects should exist only during an explicitly documented
-- transaction/recovery window.
SELECT bo.Id, bo.OwnerUserId, bo.ByteSize
FROM BlobObjects AS bo
LEFT JOIN BlobReferences AS br ON br.BlobObjectId = bo.Id
GROUP BY bo.Id, bo.OwnerUserId, bo.ByteSize
HAVING COUNT(br.Id) = 0;

-- 13. Per-owner content dedupe uniqueness check.
SELECT OwnerUserId, HashAlgorithm, ContentHash, COUNT(*) AS DuplicateCount
FROM BlobObjects
GROUP BY OwnerUserId, HashAlgorithm, ContentHash
HAVING COUNT(*) > 1;

-- 14. Work hierarchy cross-board or cross-owner violations.
SELECT child.Id, child.BoardId, parent.Id, parent.BoardId
FROM Cards AS child
JOIN Cards AS parent ON parent.Id = child.ParentCardId
WHERE child.BoardId <> parent.BoardId OR child.UserId <> parent.UserId;

-- 15. Self-parent and obvious two-node cycles.
SELECT Id FROM Cards WHERE ParentCardId = Id;
SELECT a.Id AS A, b.Id AS B
FROM Cards AS a
JOIN Cards AS b ON b.Id = a.ParentCardId
WHERE b.ParentCardId = a.Id;

-- For deeper cycles/depth, use the recursive proof in migration tests, not only ad hoc SQL.
