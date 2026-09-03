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
  /** Whether the API response confirmed persistence or left it unknown. */
  outcome: 'persisted' | 'indeterminate'
  /** The active proposal/save generation still owns this continuation. */
  current: boolean
}

export function useProposalRevisions(
  activeProposal: Ref<ApiProposal | null>,
  /**
   * `onRevisionSaved` fires the instant a save is persisted and accepted. The
   * review queue uses it to invalidate reads that started before the save: a
   * queue GET in flight at that moment still carries the pre-revision summary,
   * operations and latestRevisionId, and writing it would silently undo the
   * saved edit on screen (#2194 review round).
   */
  options?: { onRevisionSaved?: () => void },
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
      revisionCount.value = revisions.length
      latestRevision.value =
        revisions.length > 0
          ? revisions.reduce((a, b) => (a.revisionNumber > b.revisionNumber ? a : b))
          : null
      revisionsLoaded.value = true
    } catch (e: unknown) {
      if (gen !== loadGeneration || activeProposal.value?.id !== proposalId) return
      revisionCount.value = 0
      latestRevision.value = null
      // Leave revisionsLoaded false: the count is not authoritative, so callers
      // must fetch (let the backend decide) rather than short-circuit to a no-op.
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
      // This callback invalidates queue reads that could otherwise restore a
      // pre-save summary. A persisted response still needs that protection
      // when the UI continuation is stale after a proposal switch.
      options?.onRevisionSaved?.()
      if (!current) return { proposalId, outcome: 'persisted', current: false }
      // Invalidate any in-flight revision load so a pre-save (stale, empty) list
      // can't overwrite this save's state when it resolves after the save.
      loadGeneration += 1
      // Same hazard, different list: a review-queue read that predates this save
      // would restore the pre-revision proposal. Called synchronously here, in
      // the same continuation as the POST, so no queue answer can slip between
      // the save landing and the invalidation.
      latestRevision.value = revision
      revisionCount.value += 1
      revisionsLoaded.value = true
      editing.value = false
      toast.success('Revision saved')
      return { proposalId, outcome: 'persisted', current: true }
    } catch (e: unknown) {
      const current = gen === saveGeneration && activeProposal.value?.id === proposalId
      const sameProposal = activeProposal.value?.id === proposalId
      if (sameProposal) {
        // The rejected POST may have committed, so any revision GET that was
        // already in flight is not authoritative. Invalidate its generation
        // and expose an unknown state rather than certifying stale metadata.
        loadGeneration += 1
        revisionCount.value = 0
        latestRevision.value = null
        revisionsLoaded.value = false
      }
      if (current) {
        toast.error(getErrorDisplay(e, 'Failed to save revision').message)
      }
      // A rejected POST only tells us that no response was received. The server
      // may have committed before a timeout, network break, or 5xx reached the
      // client, so never certify non-persistence here.
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
