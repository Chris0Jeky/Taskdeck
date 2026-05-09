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

  async function loadRevisionState(proposalId: string) {
    try {
      const revisions = await proposalRevisionsApi.getRevisions(proposalId)
      revisionCount.value = revisions.length
      latestRevision.value =
        revisions.length > 0
          ? revisions.reduce((a, b) => (a.revisionNumber > b.revisionNumber ? a : b))
          : null
    } catch (e: unknown) {
      revisionCount.value = 0
      latestRevision.value = null
      toast.error(getErrorDisplay(e, 'Failed to load revision history').message)
    }
  }

  watch(
    () => activeProposal.value?.id,
    (id) => {
      editing.value = false
      if (id) {
        void loadRevisionState(id)
      } else {
        revisionCount.value = 0
        latestRevision.value = null
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

    try {
      saving.value = true
      const revision = await proposalRevisionsApi.createRevision(proposal.id, payload)
      latestRevision.value = revision
      revisionCount.value += 1
      editing.value = false
      toast.success('Revision saved')
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to save revision').message)
    } finally {
      saving.value = false
    }
  }

  return {
    editing,
    saving,
    revisionCount,
    latestRevision,
    startEditing,
    cancelEditing,
    saveRevision,
    loadRevisionState,
  }
}
