import { computed, ref, watch, type Ref } from 'vue'
import { automationApi } from '../api/automationApi'
import { i18n } from '../i18n'
import { useToastStore } from '../store/toastStore'
import type {
  BatchExecuteProposalResult,
  BatchExecuteProposalSelection,
  Proposal,
} from '../types/automation'
import { getErrorDisplay } from './useErrorMapper'
import { proposalIdsEqual } from '../utils/proposalIdentity'

/** The server's own bound, mirrored so the client never posts a request it knows will 400. */
const MAX_BATCH_EXECUTE_COUNT = 500

/** A receipt row joined to the title the reviewer saw, for the per-item receipt list. */
export interface BatchExecuteReceiptRow extends BatchExecuteProposalResult {
  title: string
}

function isExactApproved(status: Proposal['status']): boolean {
  return status === 1 || (typeof status === 'string' && status.toLowerCase() === 'approved')
}

/**
 * Paper's fail-closed batch-execute boundary. Deliberately narrower than what single Apply accepts:
 * an unknown wire status is never read as Approved, and only the reviewer's own approved, live,
 * non-deferred proposals with at least one operation are offered for a bulk apply. Eligibility is
 * presentation-only — the server repeats board access, status, policy, and the approved-revision
 * pin authoritatively for every item.
 */
export function isBatchExecuteEligible(
  proposal: Proposal,
  currentUserId: string | null,
  nowMs: number,
): boolean {
  if (!currentUserId || !proposalIdsEqual(proposal.requestedByUserId, currentUserId)) return false
  if (!isExactApproved(proposal.status)) return false
  if (proposal.isExpired === true) return false

  const expiresAt = new Date(proposal.expiresAt).getTime()
  if (!Number.isFinite(expiresAt) || expiresAt <= nowMs) return false

  if (proposal.deferredUntil) {
    const deferredUntil = new Date(proposal.deferredUntil).getTime()
    if (!Number.isFinite(deferredUntil) || deferredUntil > nowMs) return false
  }

  // A zero-operation approved proposal has nothing to apply; offering it in a bulk action would
  // manufacture a receipt for a write that never existed (#1423 precedent).
  return Array.isArray(proposal.operations) && proposal.operations.length > 0
}

/**
 * The "Apply approved" half of the Paper review batch rail (#1307, q-14 C).
 *
 * ADR-0003 / GP-06 are untouched: this composable never approves anything. It executes proposals
 * that are ALREADY Approved, behind one explicit confirmation, and it reports what the server
 * actually did per proposal rather than assuming the batch succeeded as a unit.
 */
export function useBatchExecuteProposals(
  proposals: Ref<Proposal[]>,
  currentUserId: Ref<string | null>,
  nowMs: Ref<number>,
  loadProposals: () => Promise<void>,
  resolveTitle?: (proposal: Proposal) => string,
) {
  const toast = useToastStore()
  const t = i18n.global.t
  const confirmationOpen = ref(false)
  const busy = ref(false)
  const receipts = ref<BatchExecuteReceiptRow[]>([])

  const eligible = computed<Proposal[]>(() =>
    proposals.value.filter((proposal) =>
      isBatchExecuteEligible(proposal, currentUserId.value, nowMs.value),
    ),
  )

  const executableCount = computed(() => Math.min(eligible.value.length, MAX_BATCH_EXECUTE_COUNT))

  function titleFor(proposal: Proposal): string {
    return resolveTitle?.(proposal) ?? proposal.presentation?.plainSummary ?? proposal.summary
  }

  function clearReceipts() {
    receipts.value = []
  }

  function requestConfirmation() {
    if (busy.value) return
    if (executableCount.value === 0) {
      toast.info(t('review.batchExecute.nothingToApply'))
      return
    }
    clearReceipts()
    confirmationOpen.value = true
  }

  function cancelConfirmation() {
    if (busy.value) return
    confirmationOpen.value = false
    clearReceipts()
  }

  watch([proposals, currentUserId, nowMs], () => {
    // The confirmation belongs to a set the reviewer saw. If the queue moved under it and there is
    // nothing left to apply, close it rather than confirm an empty batch. Receipts survive: they
    // describe what already happened and are the only record of a partial outcome.
    if (confirmationOpen.value && executableCount.value === 0) confirmationOpen.value = false
  }, { deep: true })

  async function refreshProposalsBestEffort() {
    try {
      await loadProposals()
    } catch {
      // The per-item receipts remain authoritative. A secondary refresh failure must not turn a
      // real partial success into a reported failure, nor hide the primary outcome.
    }
  }

  async function confirmExecute() {
    if (busy.value || !confirmationOpen.value) return

    const submitted = eligible.value.slice(0, MAX_BATCH_EXECUTE_COUNT)
    if (submitted.length === 0) {
      confirmationOpen.value = false
      toast.info(t('review.batchExecute.nothingToApply'))
      return
    }

    const titles = new Map(submitted.map((proposal) => [proposal.id, titleFor(proposal)]))
    const selections: BatchExecuteProposalSelection[] = submitted.map((proposal) => ({
      proposalId: proposal.id,
      // Echoed verbatim, null included: the server compares it to the pin it holds and fails that
      // item closed on a mismatch. Never substitute latestRevisionId here.
      approvedRevisionId: proposal.approvedRevisionId,
      idempotencyKey: newIdempotencyKey(),
    }))

    busy.value = true
    try {
      const result = await automationApi.executeProposals(selections)
      receipts.value = result.results.map((item) => ({
        ...item,
        title: titles.get(item.proposalId) ??
          [...titles.entries()].find(([id]) => proposalIdsEqual(id, item.proposalId))?.[1] ??
          t('review.batchExecute.unknownProposal'),
      }))

      const applied = receipts.value.filter((item) => item.outcome === 'Applied').length
      const failed = receipts.value.filter((item) => item.outcome === 'Failed').length
      if (failed === 0) {
        toast.success(t('review.batchExecute.allApplied', { count: applied }, applied))
      } else if (applied === 0) {
        toast.error(t('review.batchExecute.noneApplied', { count: failed }, failed))
      } else {
        toast.info(t('review.batchExecute.partial', { applied, failed }))
      }

      await refreshProposalsBestEffort()
    } catch (error: unknown) {
      // A whole-request rejection (400/403/5xx) applied nothing. Report it as the error it is and
      // leave no receipts behind that could be misread as a partial apply.
      clearReceipts()
      confirmationOpen.value = false
      await refreshProposalsBestEffort()
      toast.error(getErrorDisplay(error, t('review.batchExecute.failed')).message)
    } finally {
      busy.value = false
    }
  }

  return {
    eligible,
    executableCount,
    confirmationOpen,
    busy,
    receipts,
    requestConfirmation,
    cancelConfirmation,
    confirmExecute,
    clearReceipts,
  }
}

/**
 * One key per proposal per attempt. `crypto.randomUUID` is not available on every target the app
 * builds for (older Safari, non-secure contexts), and a batch that silently reused one key across
 * items would let the server's already-applied short circuit swallow real applies.
 */
function newIdempotencyKey(): string {
  const cryptoRef = globalThis.crypto as Crypto | undefined
  if (cryptoRef && typeof cryptoRef.randomUUID === 'function') return cryptoRef.randomUUID()
  return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}-${Math.random().toString(16).slice(2)}`
}
