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
  // False until the revision list for the active proposal has settled (success or
  // failure). Consumers must not treat a still-loading revisionCount of 0 as
  // "no revision" — a proposal with a saved edit renders a revision-aware diff (#1235).
  const revisionsLoaded = ref(false)
  let loadGeneration = 0
  let saveGeneration = 0

  async function loadRevisionState(proposalId: string) {
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
      revisionsLoaded.value = true
      toast.error(getErrorDisplay(e, 'Failed to load revision history').message)
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
      latestRevision.value = revision
      revisionCount.value += 1
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
