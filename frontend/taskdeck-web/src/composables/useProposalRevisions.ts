import { ref, watch, type Ref } from 'vue'
import {
  proposalRevisionsApi,
  type ProposalRevision,
  type CreateRevisionPayload,
} from '../api/proposalRevisionsApi'
import { useToastStore } from '../store/toastStore'
import { getErrorDisplay } from './useErrorMapper'
import type { Proposal as ApiProposal } from '../types/automation'

export function useProposalRevisions(activeProposal: Ref<ApiProposal | null>) {
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

  watch(
    () => activeProposal.value?.id,
    (id) => {
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

  async function saveRevision(payload: CreateRevisionPayload) {
    const proposal = activeProposal.value
    if (!proposal) return

    const proposalId = proposal.id
    const gen = ++saveGeneration
    try {
      saving.value = true
      const revision = await proposalRevisionsApi.createRevision(proposalId, payload)
      if (gen !== saveGeneration || activeProposal.value?.id !== proposalId) return
      // Invalidate any in-flight revision load so a pre-save (stale, empty) list
      // can't overwrite this save's state when it resolves after the save.
      loadGeneration += 1
      latestRevision.value = revision
      revisionCount.value += 1
      revisionsLoaded.value = true
      editing.value = false
      toast.success('Revision saved')
    } catch (e: unknown) {
      if (gen !== saveGeneration || activeProposal.value?.id !== proposalId) return
      toast.error(getErrorDisplay(e, 'Failed to save revision').message)
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
