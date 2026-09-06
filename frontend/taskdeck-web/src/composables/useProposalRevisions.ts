import { ref, watch, type Ref } from 'vue'
import {
  proposalRevisionsApi,
  type ProposalRevision,
  type CreateRevisionPayload,
} from '../api/proposalRevisionsApi'
import { useToastStore } from '../store/toastStore'
import { getErrorDisplay } from './useErrorMapper'
import type { Proposal as ApiProposal } from '../types/automation'
import {
  proposalRevisionIdentity,
  proposalRevisionMoved,
} from '../utils/proposalIdentity'

export type SaveRevisionResult = {
  proposalId: string
  /** Whether the API response confirmed persistence, rejection, or left it unknown. */
  outcome: 'persisted' | 'rejected' | 'indeterminate'
  /** The active proposal/save generation still owns this continuation. */
  current: boolean
}

/**
 * These statuses are emitted before the revision write can commit. Other
 * statuses remain indeterminate because the server may have committed before
 * the client received an error (notably 404/409 from a concurrent or deleted
 * proposal, and 5xx responses after the write path was reached).
 */
function isDefiniteRevisionSaveRejection(error: unknown): boolean {
  if (typeof error !== 'object' || error === null) return false
  const status = (error as { response?: { status?: unknown } }).response?.status
  return (
    typeof status === 'number' &&
    [400, 401, 403, 413, 422, 429].includes(status)
  )
}

type RevisionHistory = {
  revisions: Map<number, ProposalRevision>
  loaded: boolean
  invalid: boolean
}

type RevisionMetadata = {
  count: number
  latest: ProposalRevision | null
}

export function useProposalRevisions(
  activeProposal: Ref<ApiProposal | null>,
  /**
   * `onRevisionSaved` fires the instant a save is persisted and accepted. The
   * review queue uses it to invalidate reads that started before the save: a
   * queue GET in flight at that moment still carries the pre-revision summary,
   * operations and latestRevisionId, and writing it would silently undo the
   * saved edit on screen (#2194 review round).
   *
   * `onRevisionStateUncertain` fires when a POST response does not prove whether
   * the server committed. It invalidates the same pre-write queue reads without
   * claiming a successful save or changing the editor's retryable draft.
   */
  options?: {
    onRevisionSaved?: () => void
    onRevisionStateUncertain?: () => void
  },
) {
  const toast = useToastStore()

  const editing = ref(false)
  const saving = ref(false)
  const revisionCount = ref(0)
  const latestRevision = ref<ProposalRevision | null>(null)
  // False until the revision list for the active proposal has been authoritatively
  // loaded. Stays false while loading AND if the load fails, so consumers never
  // treat a not-yet-known revisionCount of 0 as "no revision" — a proposal with a
  // saved edit renders a revision-aware diff (#1235).
  const revisionsLoaded = ref(false)
  let loadGeneration = 0
  let saveGeneration = 0

  // Successful save responses survive A -> B -> A navigation so a delayed
  // response can be reconciled with the response that became current later.
  // The map is deliberately conservative: a gap in the chain stays unknown.
  const revisionHistoryByProposal = new Map<string, RevisionHistory>()

  function getRevisionHistory(proposalId: string): RevisionHistory {
    const existing = revisionHistoryByProposal.get(proposalId)
    if (existing) return existing
    const created: RevisionHistory = {
      revisions: new Map(),
      loaded: false,
      invalid: false,
    }
    revisionHistoryByProposal.set(proposalId, created)
    return created
  }

  function isUsableRevision(revision: ProposalRevision, proposalId: string): boolean {
    return (
      revision.proposalId === proposalId &&
      Number.isInteger(revision.revisionNumber) &&
      revision.revisionNumber >= 1
    )
  }

  function mergeRevision(
    history: RevisionHistory,
    proposalId: string,
    revision: ProposalRevision,
  ) {
    if (!isUsableRevision(revision, proposalId)) {
      history.invalid = true
      return
    }

    const existing = history.revisions.get(revision.revisionNumber)
    if (existing && existing.id !== revision.id) {
      history.invalid = true
      return
    }
    history.revisions.set(revision.revisionNumber, revision)
  }

  /**
   * A revision GET is the authoritative answer for the numbers it reports, so an
   * internally consistent response clears an earlier inconsistency instead of
   * leaving the proposal unknown for the whole composable lifetime (#2524 (2)):
   * badges and diff panes came back only after a remount. A response that
   * contradicts itself — a foreign proposal, a malformed number, or one number
   * under two ids — is not trusted at all: nothing from it is stored and the
   * metadata stays unknown until a consistent answer arrives. Numbers the
   * response does not cover keep the revisions their POST responses proved, so a
   * GET that predates a save still cannot erase it.
   *
   * "Consistent" is the stricter reading: the response must be a whole chain of
   * its own, 1..max or an explicit empty list. A partial answer with a gap is
   * still merged, because each of its revisions is trustworthy, but it does not
   * clear the flag — it proves nothing about the number that was disputed.
   */
  function mergeLoadedRevisions(proposalId: string, revisions: ProposalRevision[]) {
    const history = getRevisionHistory(proposalId)
    const loaded = new Map<number, ProposalRevision>()
    let highestLoaded = 0
    for (const revision of revisions) {
      if (!isUsableRevision(revision, proposalId)) {
        history.invalid = true
        history.loaded = true
        return
      }
      const conflicting = loaded.get(revision.revisionNumber)
      if (conflicting && conflicting.id !== revision.id) {
        history.invalid = true
        history.loaded = true
        return
      }
      loaded.set(revision.revisionNumber, revision)
      if (revision.revisionNumber > highestLoaded) highestLoaded = revision.revisionNumber
    }

    if (loaded.size === highestLoaded) history.invalid = false
    for (const [revisionNumber, revision] of loaded) {
      history.revisions.set(revisionNumber, revision)
    }
    history.loaded = true
  }

  function rememberPersistedRevision(proposalId: string, revision: ProposalRevision) {
    mergeRevision(getRevisionHistory(proposalId), proposalId, revision)
  }

  function getCompleteRevisionMetadata(proposalId: string): RevisionMetadata | null {
    const history = revisionHistoryByProposal.get(proposalId)
    if (!history || history.invalid) return null

    const revisionNumbers = [...history.revisions.keys()].sort((a, b) => a - b)
    if (revisionNumbers.length === 0) {
      return history.loaded ? { count: 0, latest: null } : null
    }

    const highestRevisionNumber = revisionNumbers[revisionNumbers.length - 1]
    for (let revisionNumber = 1; revisionNumber <= highestRevisionNumber; revisionNumber += 1) {
      if (!history.revisions.has(revisionNumber)) return null
    }

    return {
      count: highestRevisionNumber,
      latest: history.revisions.get(highestRevisionNumber) ?? null,
    }
  }

  /**
   * A successful POST proves only the returned revision. Publish metadata only
   * when the stored responses and/or a completed GET prove every prior number;
   * otherwise leave the state unknown and request one authoritative reload.
   *
   * A revision GET already in flight is deliberately NOT suppressed here (#2524
   * (1)). It can carry a revision another session saved while this POST was in
   * flight, and `mergeLoadedRevisions` only ever adds to what the POST proved,
   * so a pre-save answer cannot lower the published state while a strictly newer
   * one is no longer thrown away: bumping the generation here published a
   * complete-looking prefix and let the next edit build on a superseded revision
   * until the ~15 s poll moved `latestRevisionId`.
   *
   * That in-flight GET can also FAIL, and its catch then runs against a state
   * this function has already published. `loadRevisionState` therefore re-checks
   * the generation and the active proposal on BOTH paths, and its catch clears
   * metadata only when nothing authoritative has been published — a failed read
   * must not turn a proven revision count into an authoritative zero.
   */
  function publishPersistedRevisionMetadata(proposalId: string): boolean {
    if (activeProposal.value?.id !== proposalId) return false
    const metadata = getCompleteRevisionMetadata(proposalId)
    if (!metadata) {
      revisionCount.value = 0
      latestRevision.value = null
      revisionsLoaded.value = false
      return false
    }
    revisionCount.value = metadata.count
    latestRevision.value = metadata.latest
    revisionsLoaded.value = true
    return true
  }

  /**
   * A save can outlive an A -> B -> A navigation. If A is active again when its
   * old continuation lands, its current metadata may have been read before that
   * save committed. Mark only that matching active state unknown and suppress
   * its pending GET; a B selection must keep its own authoritative metadata.
   */
  function invalidateActiveRevisionMetadata(proposalId: string) {
    if (activeProposal.value?.id !== proposalId) return false
    loadGeneration += 1
    revisionCount.value = 0
    latestRevision.value = null
    revisionsLoaded.value = false
    return true
  }

  // `silent: true` suppresses the failure toast — for augment-only callers
  // (e.g. the read-only stored preview, which is already rendered locally and
  // only uses revision metadata to gate a disclosure caveat, #1397 round 3):
  // a failed metadata GET must not error-toast over a perfectly presentable
  // preview. Authoritative callers (the approve guard, the live diff path)
  // stay loud.
  async function loadRevisionState(proposalId: string, options?: { silent?: boolean }) {
    const gen = ++loadGeneration
    try {
      const revisions = await proposalRevisionsApi.getRevisions(proposalId)
      if (gen !== loadGeneration || activeProposal.value?.id !== proposalId) return
      mergeLoadedRevisions(proposalId, revisions)
      const metadata = getCompleteRevisionMetadata(proposalId)
      if (!metadata) {
        revisionCount.value = 0
        latestRevision.value = null
        revisionsLoaded.value = false
        return
      }
      revisionCount.value = metadata.count
      latestRevision.value = metadata.latest
      revisionsLoaded.value = true
    } catch (e: unknown) {
      if (gen !== loadGeneration || activeProposal.value?.id !== proposalId) return
      // A failed GET proves nothing, so it may neither publish nor destroy. It
      // clears only metadata that was ALREADY non-authoritative, leaving
      // `revisionsLoaded` false so callers fetch (let the backend decide) rather
      // than short-circuit to a no-op.
      //
      // Zeroing unconditionally was safe only while every path into this catch
      // had already dropped `revisionsLoaded`. Since a save can now publish a
      // proven chain with a GET still in flight (#2524), that GET's rejection
      // would leave `revisionsLoaded` true beside a zeroed count: PaperReviewView
      // renders the diff as no-operations, blocks Apply with a false zero-op
      // toast, and pins the editor to the pre-revision operations, so the next
      // save silently discards the revision that was already persisted.
      //
      // Republishing from the recorded history instead would be wrong the other
      // way: the resync path drops `revisionsLoaded` precisely because the
      // proposal moved to a revision this history has never seen, and
      // re-publishing the old chain there is the false authority #2215 round 1
      // M-1 forbids.
      if (!revisionsLoaded.value) {
        revisionCount.value = 0
        latestRevision.value = null
      }
      if (!options?.silent) {
        toast.error(getErrorDisplay(e, 'Failed to load revision history').message)
      }
    }
  }

  // Keyed on the proposal AND its effective revision (#2215 B). Watching the id
  // alone left this state stale whenever a background queue poll brought in a
  // revision another session had saved: `revisionCount` kept the count from
  // entry, and `editablePayload` kept preferring the cached earlier
  // `latestRevision`, so opening Edit and saving would build a newer revision
  // out of operations the server had already superseded.
  watch(
    () => [activeProposal.value?.id, proposalRevisionIdentity(activeProposal.value)] as const,
    ([id, revisionId], previous) => {
      const previousId = previous?.[0]
      // The getter builds a fresh tuple on every evaluation, so this watcher
      // also fires when a poll replaces the proposal OBJECT with an equivalent
      // one. Only a genuine move of the EFFECTIVE revision does any work —
      // otherwise every 15 s tick would re-read the revision list for an
      // unchanged proposal, and (round 2) every approval of a revised proposal
      // would look like a collaborator edit, because `latestRevisionId` is
      // nulled on the wire the moment the proposal leaves PendingReview.
      if (previous && id === previousId) {
        if (!proposalRevisionMoved(previous[1] ?? null, revisionId)) return
        // Same proposal, newer revision. Resync the authoritative state without
        // the full reset below: the reviewer may have the editor open, and
        // clearing `editing` here would close a composer mid-sentence over a
        // change that happened elsewhere.
        loadGeneration += 1
        // The count is no longer authoritative until this load answers, and the
        // load can FAIL — its catch zeroes `revisionCount` and nulls
        // `latestRevision`. Leaving `revisionsLoaded` true would publish that
        // failure as fact: `PaperReviewView` would short-circuit Apply with a
        // false zero-op toast, and `editablePayload` would fall back to the raw
        // pre-revision operations. False is the honest state — consumers then
        // let the backend decide rather than short-circuiting.
        revisionsLoaded.value = false
        // Silent: this load is driven by a background poll the reviewer never
        // asked for, and `refreshProposals` deliberately raises no toast for
        // one. An error toast here would break that doctrine from the far side
        // of the same tick.
        if (id) void loadRevisionState(id, { silent: true })
        return
      }
      loadGeneration += 1
      saveGeneration += 1
      editing.value = false
      saving.value = false
      revisionCount.value = 0
      latestRevision.value = null
      revisionsLoaded.value = false
      if (id) {
        void loadRevisionState(id)
      }
    },
    { immediate: true },
  )

  function startEditing() {
    if (!activeProposal.value) return
    editing.value = true
  }

  function cancelEditing() {
    editing.value = false
  }

  async function saveRevision(payload: CreateRevisionPayload): Promise<SaveRevisionResult | null> {
    const proposal = activeProposal.value
    if (!proposal) return null

    const proposalId = proposal.id
    const gen = ++saveGeneration
    try {
      saving.value = true
      const revision = await proposalRevisionsApi.createRevision(proposalId, payload)
      const current = gen === saveGeneration && activeProposal.value?.id === proposalId
      rememberPersistedRevision(proposalId, revision)
      const metadataKnown = publishPersistedRevisionMetadata(proposalId)
      if (!current) {
        // The persisted A1 response is stale as an editor continuation, but it
        // still makes matching re-entered A metadata (and any pending A GET)
        // unsafe to treat as an authoritative empty revision list.
        if (!metadataKnown && activeProposal.value?.id === proposalId) {
          void loadRevisionState(proposalId)
        }
        // Same hazard, different list: a queue GET that predates this save must
        // not restore the pre-revision proposal, even if this UI continuation is stale.
        options?.onRevisionSaved?.()
        return { proposalId, outcome: 'persisted', current: false }
      }
      // A revision GET still in flight is left to land: when it answers it can
      // only add to the revisions this save proved, and it may carry a newer one
      // (#2524). When it FAILS instead, its catch preserves what was published
      // here rather than zeroing it.
      // Same hazard, different list: a review-queue read that predates this save
      // would restore the pre-revision proposal. Called synchronously here, in
      // the same continuation as the POST, so no queue answer can slip between
      // the save landing and the invalidation.
      options?.onRevisionSaved?.()
      if (!metadataKnown) {
        void loadRevisionState(proposalId)
      }
      editing.value = false
      toast.success('Revision saved')
      return { proposalId, outcome: 'persisted', current: true }
    } catch (e: unknown) {
      const current = gen === saveGeneration && activeProposal.value?.id === proposalId
      if (isDefiniteRevisionSaveRejection(e)) {
        // The API rejected this request before reporting persistence. Keep the
        // authoritative metadata and retryable draft intact; unlike an
        // indeterminate response, no queue read can have become stale.
        if (current) {
          toast.error(getErrorDisplay(e, 'Failed to save revision').message)
        }
        return { proposalId, outcome: 'rejected', current }
      }
      // A rejected POST can have committed before a timeout, network break, or
      // 5xx reached the client. Invalidate queue reads synchronously even for a
      // stale continuation: a pre-write answer can restore old operations after
      // an unseen commit. This is uncertainty, not a success notification.
      options?.onRevisionStateUncertain?.()
      // Its active proposal's revision metadata is then unknown, but the editor
      // draft remains a separate concern for the view.
      invalidateActiveRevisionMetadata(proposalId)
      if (current) {
        toast.error(getErrorDisplay(e, 'Failed to save revision').message)
      }
      return { proposalId, outcome: 'indeterminate', current }
    } finally {
      if (gen === saveGeneration && activeProposal.value?.id === proposalId) {
        saving.value = false
      }
    }
  }

  return {
    editing,
    saving,
    revisionCount,
    revisionsLoaded,
    latestRevision,
    startEditing,
    cancelEditing,
    saveRevision,
    loadRevisionState,
  }
}
