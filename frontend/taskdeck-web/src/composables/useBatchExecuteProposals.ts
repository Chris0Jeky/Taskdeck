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
import {
  isBoundedCreateCardOnly,
  isExactLowRisk,
  isLiveAndNotDeferred,
  isOwnBatchProposal,
} from './batchProposalEligibility'

/** The server's own bound, mirrored so the client never posts a request it knows will 400. */
const MAX_BATCH_EXECUTE_COUNT = 500

/** A receipt row joined to the title the reviewer saw, for the per-item receipt list. */
export interface BatchExecuteReceiptRow extends BatchExecuteProposalResult {
  title: string
}

interface CapturedBatchExecuteSelection {
  proposalId: string
  approvedRevisionId: string | null
  title: string
}

function isExactApproved(status: Proposal['status']): boolean {
  return status === 1 || (typeof status === 'string' && status.toLowerCase() === 'approved')
}

/**
 * Paper's fail-closed batch-execute boundary, and deliberately much narrower than what single Apply
 * accepts.
 *
 * It admits exactly the class of work its sibling batch approve admits - the SHARED gates in
 * `batchProposalEligibility`: the reviewer's own proposal, exactly Low risk, live and not deferred,
 * and a bounded set of card creations only. #1307 AC3 scopes both halves to "eligible low-risk,
 * create-card-only proposals", and without the risk and operation-shape gates a single click on
 * *Apply approved* would reach approved High/Critical archive or bulk-move proposals - the exact
 * decisions a bulk action is unsuited to make. The zero-operation case falls out of the same gate:
 * offering a proposal with nothing to apply would manufacture a receipt for a write that never
 * existed (#1423 precedent).
 *
 * The one axis that differs from approve is the status, which is the whole point of this surface:
 * exactly Approved, never a normalized guess - an unknown wire status is not read as Approved.
 *
 * Widening this - bulk-applying higher-risk or non-create proposals - is a product decision for the
 * maintainer, not an implementation detail; it is flagged on #1307 rather than assumed here.
 *
 * Eligibility is presentation-only: the server repeats board access, status, policy, and the
 * approved-revision pin authoritatively for every item, and it does NOT impose this narrowing, so
 * single Apply is unaffected.
 */
export function isBatchExecuteEligible(
  proposal: Proposal,
  currentUserId: string | null,
  nowMs: number,
): boolean {
  if (!isOwnBatchProposal(proposal, currentUserId)) return false
  if (!isExactApproved(proposal.status) || !isExactLowRisk(proposal.riskLevel)) return false
  if (!isLiveAndNotDeferred(proposal, nowMs)) return false

  return isBoundedCreateCardOnly(proposal.operations)
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
  reviewScopeKey: Ref<string>,
  resolveTitle?: (proposal: Proposal) => string,
) {
  const toast = useToastStore()
  const t = i18n.global.t
  const confirmationOpen = ref(false)
  const busy = ref(false)
  const receipts = ref<BatchExecuteReceiptRow[]>([])
  const capturedSelections = ref<CapturedBatchExecuteSelection[]>([])
  const capturedScopeKey = ref<string | null>(null)

  const eligible = computed<Proposal[]>(() =>
    proposals.value.filter((proposal) =>
      isBatchExecuteEligible(proposal, currentUserId.value, nowMs.value),
    ),
  )

  const executableCount = computed(() => Math.min(eligible.value.length, MAX_BATCH_EXECUTE_COUNT))
  const confirmationCount = computed(() => capturedSelections.value.length)

  function titleFor(proposal: Proposal): string {
    return resolveTitle?.(proposal) ?? proposal.presentation?.plainSummary ?? proposal.summary
  }

  function clearReceipts() {
    receipts.value = []
  }

  function clearCapture() {
    capturedSelections.value = []
    capturedScopeKey.value = null
  }

  function captureCurrentSelection(): CapturedBatchExecuteSelection[] {
    return eligible.value.slice(0, MAX_BATCH_EXECUTE_COUNT).map((proposal) => ({
      proposalId: proposal.id,
      approvedRevisionId: proposal.approvedRevisionId,
      title: titleFor(proposal),
    }))
  }

  function approvedRevisionIdsEqual(left: string | null, right: string | null): boolean {
    return left === null && right === null || proposalIdsEqual(left, right)
  }

  function confirmationStillCurrent(): boolean {
    if (capturedSelections.value.length === 0) return false
    if (capturedScopeKey.value !== reviewScopeKey.value) return false

    const remaining = captureCurrentSelection()
    if (remaining.length !== capturedSelections.value.length) return false

    return capturedSelections.value.every((captured) => {
      const matchIndex = remaining.findIndex((current) =>
        proposalIdsEqual(current.proposalId, captured.proposalId) &&
        approvedRevisionIdsEqual(current.approvedRevisionId, captured.approvedRevisionId),
      )
      if (matchIndex < 0) return false
      remaining.splice(matchIndex, 1)
      return true
    })
  }

  function invalidateConfirmation() {
    confirmationOpen.value = false
    clearCapture()
  }

  function requestConfirmation() {
    if (busy.value) return
    if (executableCount.value === 0) {
      toast.info(t('review.batchExecute.nothingToApply'))
      return
    }
    clearReceipts()
    capturedSelections.value = captureCurrentSelection()
    capturedScopeKey.value = reviewScopeKey.value
    confirmationOpen.value = true
  }

  /**
   * The dialog's close action, for both of its phases.
   *
   * Receipts record writes that ALREADY happened, so dismissing them is always allowed - including
   * during the best-effort queue refresh that follows a batch, which keeps `busy` true for a while
   * after the applies have landed. Blocking close there left the reviewer looking at an enabled
   * Done button that did nothing. Only the pre-apply confirmation is locked while a request is in
   * flight, because closing it mid-POST would misrepresent what was decided.
   */
  function cancelConfirmation() {
    if (receipts.value.length > 0) {
      confirmationOpen.value = false
      clearReceipts()
      clearCapture()
      return
    }
    if (busy.value) return
    invalidateConfirmation()
  }

  /**
   * Unconditional close, for a context change that invalidates the whole surface rather than a
   * reviewer decision - entering archived history, where no apply may be offered at all. It ignores
   * `busy` on purpose: an in-flight response can still land, but it will land into a closed dialog
   * and its receipts are cleared by the next `requestConfirmation`.
   */
  function forceClose() {
    confirmationOpen.value = false
    clearReceipts()
    clearCapture()
  }

  watch([proposals, currentUserId, nowMs, reviewScopeKey], () => {
    // The confirmation belongs to a set the reviewer saw. If the queue moved under it and there is
    // nothing left to apply, close it rather than confirm an empty batch.
    //
    // The receipts guard is load-bearing, not defensive. Once results arrive this dialog is no
    // longer a confirmation: it IS the receipt surface, and the only place a per-item outcome is
    // ever shown. `confirmExecute` sets `receipts` and then awaits the queue refresh, which removes
    // the just-applied proposals from eligibility and drives `executableCount` to 0 - so without
    // this guard a fully successful batch closed its own receipts before the reviewer could read
    // them, and the better the batch went the more certain the receipts were to vanish. Once
    // receipts exist the dialog stays open until the reviewer dismisses it.
    if (receipts.value.length > 0) return
    // Once Confirm has been activated, the captured set is already authorized and the request may
    // legitimately overlap a realtime refresh. Pre-click drift invalidates consent; post-click
    // drift must not retract an in-flight request or destroy the receipts it is about to produce.
    if (busy.value) return
    if (confirmationOpen.value && !confirmationStillCurrent()) invalidateConfirmation()
  }, { deep: true, flush: 'sync' })

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

    // Recheck synchronously at the click boundary as well as in the watcher. A route/queue update
    // in the same tick must not substitute a different live batch before Vue schedules effects.
    if (!confirmationStillCurrent()) {
      invalidateConfirmation()
      return
    }

    const submitted = [...capturedSelections.value]
    if (submitted.length === 0) {
      invalidateConfirmation()
      toast.info(t('review.batchExecute.nothingToApply'))
      return
    }

    const titles = new Map(submitted.map((selection) => [selection.proposalId, selection.title]))
    const selections: BatchExecuteProposalSelection[] = submitted.map((proposal) => ({
      proposalId: proposal.proposalId,
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

      // The durable receipt dialog owns the full per-item outcome and its single live-region
      // announcement. A completion toast would announce the same result again and can interleave
      // with that authoritative summary. Whole-request failures still use the catch-path toast.

      await refreshProposalsBestEffort()
    } catch (error: unknown) {
      // A whole-request rejection (400/403/5xx) applied nothing. Report it as the error it is and
      // leave no receipts behind that could be misread as a partial apply.
      clearReceipts()
      confirmationOpen.value = false
      clearCapture()
      await refreshProposalsBestEffort()
      toast.error(getErrorDisplay(error, t('review.batchExecute.failed')).message)
    } finally {
      busy.value = false
    }
  }

  return {
    eligible,
    executableCount,
    confirmationCount,
    confirmationOpen,
    busy,
    receipts,
    requestConfirmation,
    cancelConfirmation,
    forceClose,
    confirmExecute,
    clearReceipts,
  }
}

/**
 * One key per proposal per attempt, mirroring exactly what N separate single-execute calls would
 * send.
 *
 * It is worth being precise about what this key does and does not do, because the obvious guess is
 * wrong: the server never stores or compares the execute-level key. `AutomationExecutorService`
 * only rejects a blank one, and the replay guard that makes a second apply a no-op is the
 * proposal's own `Applied` STATUS, not the key. So a reused key would not currently corrupt
 * anything. Distinct keys are still generated per item because that is the honest shape of the
 * request - one idempotent identity per apply - and because the endpoint rejects a batch that
 * claims one identity for two proposals.
 *
 * `crypto.randomUUID` is not available on every target the app builds for (older Safari,
 * non-secure contexts), hence the fallback.
 */
function newIdempotencyKey(): string {
  const cryptoRef = globalThis.crypto as Crypto | undefined
  if (cryptoRef && typeof cryptoRef.randomUUID === 'function') return cryptoRef.randomUUID()
  return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}-${Math.random().toString(16).slice(2)}`
}
