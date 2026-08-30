import { computed, ref, watch, type Ref } from 'vue'
import { automationApi } from '../api/automationApi'
import { i18n } from '../i18n'
import { useToastStore } from '../store/toastStore'
import type { BatchApproveProposalSelection, Proposal } from '../types/automation'
import { getErrorDisplay } from './useErrorMapper'
import { STALE_PROPOSAL_MS } from './useReviewProposals'
import { proposalIdsEqual } from '../utils/proposalIdentity'
import {
  isBoundedCreateCardOnly,
  isExactLowRisk,
  isLiveAndNotDeferred,
  isOwnBatchProposal,
} from './batchProposalEligibility'

const MAX_BATCH_APPROVAL_COUNT = 500

interface CapturedBatchSelection extends BatchApproveProposalSelection {
  reviewFingerprint: string
}

function reviewFingerprint(proposal: Proposal): string {
  return JSON.stringify({
    status: proposal.status,
    riskLevel: proposal.riskLevel,
    summary: proposal.summary,
    validationIssues: proposal.validationIssues,
    diffPreview: proposal.diffPreview,
    updatedAt: proposal.updatedAt,
    latestRevisionId: proposal.latestRevisionId,
    operations: proposal.operations,
    presentation: proposal.presentation,
  })
}

function captureSelection(proposal: Proposal): CapturedBatchSelection {
  return {
    id: proposal.id,
    expectedProposalUpdatedAt: proposal.updatedAt,
    expectedLatestRevisionId: proposal.latestRevisionId,
    reviewFingerprint: reviewFingerprint(proposal),
  }
}

function isExactPendingReview(status: Proposal['status']): boolean {
  return status === 0 || (
    typeof status === 'string' && status.toLowerCase() === 'pendingreview'
  )
}

/**
 * Paper's fail-closed batch-approve boundary. Unlike the general display normalizers, unknown wire
 * values are never treated as PendingReview/Low. Eligibility is presentation-only; the server
 * repeats every gate authoritatively at confirmation time.
 *
 * The risk, liveness, and operation-shape gates are the SHARED ones in `batchProposalEligibility`,
 * so batch approve and batch execute cannot drift apart on the class of work they admit (#1307
 * AC3). Two gates stay local on purpose: the exact PendingReview status, which is this surface's
 * whole point, and the freshness window below, which guards an undecided proposal going stale in
 * front of the reviewer and has no counterpart once a proposal has been approved.
 */
export function isBatchApproveEligible(
  proposal: Proposal,
  currentUserId: string | null,
  nowMs: number,
): boolean {
  if (!isOwnBatchProposal(proposal, currentUserId)) return false
  if (!isExactPendingReview(proposal.status) || !isExactLowRisk(proposal.riskLevel)) return false
  if (!isLiveAndNotDeferred(proposal, nowMs)) return false

  const createdAt = new Date(proposal.createdAt).getTime()
  if (!Number.isFinite(createdAt) || nowMs - createdAt >= STALE_PROPOSAL_MS) return false

  return isBoundedCreateCardOnly(proposal.operations)
}

export function useBatchApproveProposals(
  proposals: Ref<Proposal[]>,
  currentUserId: Ref<string | null>,
  nowMs: Ref<number>,
  loadProposals: () => Promise<void>,
) {
  const toast = useToastStore()
  const t = i18n.global.t
  const selectedIds = ref<Set<string>>(new Set())
  const selectedSnapshots = ref<Map<string, CapturedBatchSelection>>(new Map())
  const confirmationOpen = ref(false)
  const busy = ref(false)

  const eligibleIds = computed(() => new Set(
    proposals.value
      .filter((proposal) => isBatchApproveEligible(proposal, currentUserId.value, nowMs.value))
      .map((proposal) => proposal.id),
  ))

  const selectedCount = computed(() => selectedIds.value.size)

  function replaceSelection(snapshots: Iterable<CapturedBatchSelection>) {
    const next = [...snapshots]
    selectedSnapshots.value = new Map(next.map((snapshot) => [snapshot.id, snapshot]))
    selectedIds.value = new Set(next.map((snapshot) => snapshot.id))
  }

  function clearSelection() {
    replaceSelection([])
    confirmationOpen.value = false
  }

  function reconcileSelection(): boolean {
    const retained = [...selectedSnapshots.value.values()].filter((snapshot) => {
      const proposal = proposals.value.find((candidate) =>
        proposalIdsEqual(candidate.id, snapshot.id),
      )
      return !!proposal &&
        isBatchApproveEligible(proposal, currentUserId.value, nowMs.value) &&
        proposal.updatedAt === snapshot.expectedProposalUpdatedAt &&
        proposal.latestRevisionId === snapshot.expectedLatestRevisionId &&
        reviewFingerprint(proposal) === snapshot.reviewFingerprint
    })
    const changed = retained.length !== selectedSnapshots.value.size
    if (changed) {
      replaceSelection(retained)
      // Confirmation belongs to the exact set the reviewer saw. Even when eligible items remain,
      // pruning one member invalidates that consent and requires a fresh explicit confirmation.
      confirmationOpen.value = false
    }
    if (selectedIds.value.size === 0) confirmationOpen.value = false
    return changed
  }

  watch(
    [proposals, currentUserId, nowMs],
    () => {
      if (reconcileSelection()) toast.info(t('review.batchApprove.selectionChanged'))
    },
    { deep: true, flush: 'sync' },
  )

  function isSelected(id: string): boolean {
    return [...selectedIds.value].some((selectedId) => proposalIdsEqual(selectedId, id))
  }

  function toggleSelection(id: string) {
    if (busy.value) return
    const canonical = [...eligibleIds.value].find((eligibleId) => proposalIdsEqual(eligibleId, id))
    if (!canonical) return
    const next = new Map(selectedSnapshots.value)
    const selected = [...next.keys()].find((selectedId) => proposalIdsEqual(selectedId, canonical))
    if (selected) next.delete(selected)
    else {
      if (next.size >= MAX_BATCH_APPROVAL_COUNT) {
        toast.info(t('review.batchApprove.limitReached', { count: MAX_BATCH_APPROVAL_COUNT }))
        return
      }
      const proposal = proposals.value.find((candidate) => proposalIdsEqual(candidate.id, canonical))
      if (!proposal) return
      next.set(proposal.id, captureSelection(proposal))
    }
    replaceSelection(next.values())
  }

  function requestConfirmation() {
    if (busy.value) return
    if (reconcileSelection() || selectedIds.value.size === 0) {
      toast.info(t('review.batchApprove.selectionChanged'))
      return
    }
    confirmationOpen.value = true
  }

  function cancelConfirmation() {
    if (busy.value) return
    confirmationOpen.value = false
  }

  async function refreshProposalsBestEffort() {
    try {
      await loadProposals()
    } catch {
      // The approve receipt remains authoritative. The caller has already reconciled the visible
      // state (or deliberately left it untouched for an invalid receipt), so a secondary refresh
      // failure must not turn a successful approval into a false failure or hide the primary error.
    }
  }

  async function confirmApproval() {
    if (busy.value || !confirmationOpen.value) return

    // Revalidate against the immediately-current queue before the POST. The backend performs the
    // authoritative second revalidation, including permissions and concurrency tokens.
    if (reconcileSelection() || selectedIds.value.size === 0) {
      confirmationOpen.value = false
      toast.error(t('review.batchApprove.selectionChanged'))
      return
    }

    const submitted = [...selectedSnapshots.value.values()].map((snapshot) => ({
      id: snapshot.id,
      expectedProposalUpdatedAt: snapshot.expectedProposalUpdatedAt,
      expectedLatestRevisionId: snapshot.expectedLatestRevisionId,
    }))
    const ids = submitted.map((selection) => selection.id)
    confirmationOpen.value = false
    busy.value = true

    try {
      const result = await automationApi.approveProposals(submitted)
      const receiptMatches =
        result.approvedIds.length === ids.length &&
        ids.every((id) => result.approvedIds.some((approvedId) => proposalIdsEqual(approvedId, id)))

      replaceSelection([])
      if (!receiptMatches) {
        await refreshProposalsBestEffort()
        toast.error(t('review.batchApprove.receiptMismatch'))
        return
      }

      // The receipt proves the decision but does not carry the pinned effective content. Remove the
      // stale pending rows before refreshing instead of manufacturing Approved/execute-ready rows
      // from a snapshot that may no longer be current. A failed refresh therefore remains fail-closed.
      proposals.value = proposals.value.filter((proposal) =>
        !result.approvedIds.some((id) => proposalIdsEqual(id, proposal.id)),
      )
      toast.success(
        t('review.batchApprove.approved', { count: ids.length }, ids.length),
        undefined,
        { label: 'approved' },
      )
      await refreshProposalsBestEffort()
    } catch (error: unknown) {
      // A server-side drift/conflict is authoritative. Refresh before reporting it so the queue does
      // not continue advertising stale selection, but never claim any item was approved locally.
      replaceSelection([])
      await refreshProposalsBestEffort()
      toast.error(getErrorDisplay(error, t('review.batchApprove.failed')).message)
    } finally {
      busy.value = false
    }
  }

  return {
    eligibleIds,
    selectedIds,
    selectedCount,
    confirmationOpen,
    busy,
    clearSelection,
    isSelected,
    toggleSelection,
    requestConfirmation,
    cancelConfirmation,
    confirmApproval,
  }
}
