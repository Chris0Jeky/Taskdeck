import { ref, type ComputedRef, type Ref } from 'vue'
import { automationApi } from '../api/automationApi'
import { useToastStore } from '../store/toastStore'
import { createRequestId } from '../utils/requestId'
import { normalizeProposalRiskLevel } from '../utils/automation'
import type { Proposal as ApiProposal } from '../types/automation'
import { getErrorDisplay } from './useErrorMapper'
import { usePerformanceMark } from './usePerformanceMark'

export function useReviewActions(
  proposals: Ref<ApiProposal[]>,
  dismissableProposalIds: ComputedRef<string[]>,
  loadProposals: () => Promise<void>,
) {
  const toast = useToastStore()
  const diffRenderPerf = usePerformanceMark('proposal-diff-render')

  const proposalActionBusyId = ref<string | null>(null)
  // Bulk dismiss has no single proposal id to track, so it gets its own
  // in-flight flag. Surfaced into the shared `busy` state so the bulk button
  // disables, re-entry is blocked, and the keymap/per-proposal actions are
  // gated while a bulk clear is running. #1161
  const bulkDismissBusy = ref(false)
  const selectedDiffProposalId = ref<string | null>(null)
  const selectedDiff = ref<string | null>(null)
  let latestDiffRequestId = 0

  async function handleApproveProposal(proposalId: string) {
    try {
      proposalActionBusyId.value = proposalId
      const updated = await automationApi.approveProposal(proposalId)
      proposals.value = proposals.value.map((p) => (p.id === proposalId ? updated : p))
      toast.success('Proposal approved for board application')
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to approve proposal').message)
    } finally {
      proposalActionBusyId.value = null
    }
  }

  async function handleRejectProposal(proposalId: string, riskLevel: ApiProposal['riskLevel']) {
    const requiresReason = ['High', 'Critical'].includes(normalizeProposalRiskLevel(riskLevel))
    const promptedReason = prompt(
      requiresReason ? 'Reason is required for this risk level:' : 'Optional rejection reason:',
    )
    if (promptedReason === null) return

    const reason = promptedReason.trim()
    if (requiresReason && !reason) {
      toast.error('Rejection reason is required for high and critical risk proposals')
      return
    }

    const reasonOrNull = reason.length > 0 ? reason : null

    try {
      proposalActionBusyId.value = proposalId
      const updated = await automationApi.rejectProposal(proposalId, reasonOrNull)
      proposals.value = proposals.value.map((p) => (p.id === proposalId ? updated : p))
      toast.success('Proposal rejected')
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to reject proposal').message)
    } finally {
      proposalActionBusyId.value = null
    }
  }

  async function handleDeferProposal(proposalId: string) {
    try {
      proposalActionBusyId.value = proposalId
      const updated = await automationApi.deferProposal(proposalId)
      // Map the returned proposal in place so its new deferredUntil/expiresAt are live;
      // the ~60s review clock then resurfaces it when the snooze window elapses.
      proposals.value = proposals.value.map((p) => (p.id === proposalId ? updated : p))
      toast.success('Snoozed for 1 hour — it will return to your queue.')
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to snooze proposal').message)
    } finally {
      proposalActionBusyId.value = null
    }
  }

  async function handleExecuteProposal(proposalId: string) {
    if (!confirm('Apply this approved proposal to the board now?')) return

    try {
      proposalActionBusyId.value = proposalId
      const updated = await automationApi.executeProposal(proposalId, createRequestId())
      proposals.value = proposals.value.map((p) => (p.id === proposalId ? updated : p))
      toast.success('Proposal applied to board')
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to apply proposal to board').message)
    } finally {
      proposalActionBusyId.value = null
    }
  }

  async function handleToggleDiff(proposalId: string) {
    if (selectedDiffProposalId.value === proposalId) {
      latestDiffRequestId += 1
      selectedDiffProposalId.value = null
      selectedDiff.value = null
      return
    }

    diffRenderPerf.start()
    const requestId = ++latestDiffRequestId

    try {
      selectedDiffProposalId.value = proposalId
      selectedDiff.value = null

      const diff = await automationApi.getProposalDiff(proposalId)
      if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) return

      selectedDiff.value = diff
    } catch (e: unknown) {
      if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) return

      selectedDiffProposalId.value = null
      selectedDiff.value = null
      toast.error(getErrorDisplay(e, 'Failed to load proposal diff').message)
    } finally {
      diffRenderPerf.end()
    }
  }

  async function handleDismissProposal(proposalId: string) {
    try {
      proposalActionBusyId.value = proposalId
      const result = await automationApi.dismissProposals([proposalId])
      if (result.dismissed > 0) {
        proposals.value = proposals.value.filter((p) => p.id !== proposalId)
        toast.success('Proposal dismissed')
      } else {
        proposals.value = proposals.value.filter((p) => p.id !== proposalId)
        toast.info('Proposal removed from view. Refreshing...')
        void loadProposals()
      }
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to dismiss proposal').message)
    } finally {
      proposalActionBusyId.value = null
    }
  }

  async function handleDismissApplied() {
    // Block re-entry: a second bulk request while the first is in flight would
    // race on proposals.value and fire duplicate dismiss calls.
    if (bulkDismissBusy.value) return

    const ids = dismissableProposalIds.value
    if (ids.length === 0) {
      toast.info('No completed proposals to clear.')
      return
    }

    try {
      bulkDismissBusy.value = true
      const result = await automationApi.dismissProposals(ids)
      if (result.dismissed === ids.length) {
        const dismissedSet = new Set(ids)
        proposals.value = proposals.value.filter((p) => !dismissedSet.has(p.id))
      } else {
        await loadProposals()
      }
      toast.success(`Cleared ${result.dismissed} completed proposal${result.dismissed === 1 ? '' : 's'}.`)
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to clear proposals').message)
    } finally {
      bulkDismissBusy.value = false
    }
  }

  return {
    proposalActionBusyId,
    bulkDismissBusy,
    selectedDiffProposalId,
    selectedDiff,
    handleApproveProposal,
    handleRejectProposal,
    handleDeferProposal,
    handleExecuteProposal,
    handleToggleDiff,
    handleDismissProposal,
    handleDismissApplied,
  }
}
