import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { automationApi } from '../api/automationApi'
import { i18n } from '../i18n'
import { proposalRevisionsApi } from '../api/proposalRevisionsApi'
import { useToastStore } from '../store/toastStore'
import { createRequestId } from '../utils/requestId'
import { normalizeProposalRiskLevel, normalizeProposalStatus } from '../utils/automation'
import type { Proposal as ApiProposal } from '../types/automation'
import { getErrorDisplay, getValidationReason, isAccessDeniedError, isValidationError } from './useErrorMapper'
import { isProposalReadOnly } from './useReviewProposals'
import { usePerformanceMark } from './usePerformanceMark'

/**
 * How the review diff pane presents its content (#1397):
 * - `live`    — a freshly fetched `/diff` for a still-actionable proposal.
 * - `stored`  — a read-only/terminal proposal's stored `diffPreview` (no live
 *               request; may be null → "no stored preview available").
 * - `invalid` — the backend rejected the diff (400 ValidationError); the
 *               backend's actual reason is carried in `selectedDiffInvalidReason`
 *               and Apply would reject the proposal for the same reason, so the
 *               reviewer must see that verdict rather than a generic error toast.
 */
export type ReviewDiffMode = 'live' | 'stored' | 'invalid'

// Fallback expiry classifier for callers that don't supply the surface's
// reactive clock. Status-scoped to match the canonical rule
// (useReviewProposals.isProposalExpired): the server-authoritative `isExpired`
// flag is time-based and status-AGNOSTIC on the backend, so it is trusted ONLY
// for a live PendingReview/Approved proposal — a terminal proposal whose expiry
// later passed keeps its terminal status, never flips to "Expired". It cannot
// see a still-pending proposal whose expiresAt passes mid-session without the
// server flag, so surfaces that need that (Legacy) pass their own clock fn.
function defaultIsProposalExpired(proposal: ApiProposal): boolean {
  const normalized = normalizeProposalStatus(proposal.status)
  if (normalized === 'Expired') return true
  if (normalized === 'PendingReview' || normalized === 'Approved') return proposal.isExpired === true
  return false
}

export function useReviewActions(
  proposals: Ref<ApiProposal[]>,
  dismissableProposalIds: ComputedRef<string[]>,
  loadProposals: () => Promise<void>,
  // Same client-side expiry rule the surrounding surface uses (from
  // useReviewProposals). Defaults to the server-authoritative `isExpired` flag /
  // domain Expired status so callers that don't drive a reactive clock (e.g.
  // Paper, which owns its own diff flow) still classify read-only correctly.
  isProposalExpired: (proposal: ApiProposal) => boolean = defaultIsProposalExpired,
) {
  const toast = useToastStore()
  const diffRenderPerf = usePerformanceMark('proposal-diff-render')
  /**
   * i18n via the MODULE-SCOPED runtime, not `useI18n()` (ADR-0054 / `#1770`).
   * `useI18n()` requires a live component instance; this composable is also
   * driven by the Legacy shell and exercised by specs that never mount one, and
   * threading a `t` argument through would change every caller's signature for
   * no gain. `i18n.global.t` reads `i18n.global.locale` internally, so a call
   * made inside a computed still re-evaluates on a language switch.
   */
  const t = i18n.global.t

  const proposalActionBusyId = ref<string | null>(null)
  // Bulk dismiss has no single proposal id to track, so it gets its own
  // in-flight flag. Surfaced into the shared `busy` state so the bulk button
  // disables, re-entry is blocked, and the keymap/per-proposal actions are
  // gated while a bulk clear is running. #1161
  const bulkDismissBusy = ref(false)
  const selectedDiffProposalId = ref<string | null>(null)
  const selectedDiff = ref<string | null>(null)
  const selectedDiffMode = ref<ReviewDiffMode | null>(null)
  // The backend's actual rejection reason for `invalid` mode (e.g. "Proposal has
  // expired" vs "Proposal must contain at least one operation") — the pane must
  // render the REAL reason, never a hardcoded one (#1397 MEDIUM-1).
  const selectedDiffInvalidReason = ref<string | null>(null)
  // Whether the stored preview's proposal has saved revisions: true/false once
  // known, null while unknown (fetch pending or failed). `diffPreview` is
  // creation-time content that revisions never update, so a revised proposal's
  // stored preview shows the ORIGINAL submission — the banner must disclose
  // that (#1397 MEDIUM-2).
  const selectedDiffRevised = ref<boolean | null>(null)
  let latestDiffRequestId = 0

  function resetDiffState() {
    selectedDiffProposalId.value = null
    selectedDiff.value = null
    selectedDiffMode.value = null
    selectedDiffInvalidReason.value = null
    selectedDiffRevised.value = null
  }

  // Best-effort disclosure signal for the stored preview (#1397 MEDIUM-2): fetch
  // the revision list so the banner can state when the stored (original) preview
  // is NOT what a later revision would have applied. On failure the flag stays
  // null (unknown) — the banner already attributes the content to the original
  // submission, so we fail toward the generic wording rather than blocking the
  // stored preview or false-claiming "never revised".
  async function loadStoredRevisedSignal(proposalId: string, requestId: number) {
    try {
      const revisions = await proposalRevisionsApi.getRevisions(proposalId)
      if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) return
      selectedDiffRevised.value = revisions.length > 0
    } catch {
      if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) return
      selectedDiffRevised.value = null
    }
  }

  // #1414 P2: revealing the stored `diffPreview` locally skips the `/diff` call
  // that used to re-run AuthorizeProposalAsync, so re-authorize on reveal. Render
  // the stored preview SYNCHRONOUSLY (the #1397 seam invariant: local content is
  // never network-gated), then probe access via GET proposal (which returns 200
  // for a still-readable terminal/expired proposal — unlike `/diff`, it does not
  // 400 on expiry). ONLY a genuine 403/404 retracts the preview; a transient
  // error must not tear down an inspectable local preview. The refreshed DTO is
  // deliberately NOT rendered — #1397 keeps the decision-time stored artifact
  // (a live re-render can drift). Guarded by requestId + proposal id so a late
  // response for a toggled-off / switched proposal cannot tear down the wrong pane.
  async function verifyStoredPreviewAccess(proposalId: string, requestId: number) {
    try {
      await automationApi.getProposal(proposalId)
    } catch (e: unknown) {
      if (!isAccessDeniedError(e)) return
      if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) return
      resetDiffState()
      toast.error(t('review.toast.noLongerAvailable'))
    }
  }

  function presentStoredPreview(proposal: ApiProposal, requestId: number) {
    selectedDiff.value = proposal.diffPreview
    selectedDiffMode.value = 'stored'
    selectedDiffInvalidReason.value = null
    selectedDiffRevised.value = null
    void loadStoredRevisedSignal(proposal.id, requestId)
    void verifyStoredPreviewAccess(proposal.id, requestId)
  }

  // #1397 LOW-5: the pane's presentation is chosen at toggle time, but a proposal
  // can turn read-only WHILE its pane is open — a status change (approve→execute,
  // a refresh mapping in a terminal state) or the surface's expiry clock ticking
  // past expiresAt. Re-derive: an open live/invalid pane flips to the stored
  // read-only presentation the moment the classification flips, instead of
  // keeping a live-looking pane on a proposal that is no longer actionable.
  watch(
    () => {
      const id = selectedDiffProposalId.value
      if (!id) return false
      const proposal = proposals.value.find((p) => p.id === id)
      if (!proposal) return false
      return isProposalReadOnly(proposal, isProposalExpired(proposal))
    },
    (readOnly) => {
      if (!readOnly) return
      // SEAM INVARIANT (#1397 round 3): a read-only conversion invalidates
      // EVERY non-stored pane state — including the loading state, where
      // selectedDiffMode is still null while the live /diff is in flight. Only
      // an already-stored presentation is skippable; a null mode with an open
      // pane id means a fetch is pending, and NOT converting here would let its
      // late response render live UI on a read-only proposal.
      if (selectedDiffMode.value === 'stored') return
      const id = selectedDiffProposalId.value
      const proposal = id ? proposals.value.find((p) => p.id === id) : undefined
      if (!proposal) return
      // Cancel any in-flight live fetch so its late response can't overwrite
      // the read-only presentation.
      const requestId = ++latestDiffRequestId
      presentStoredPreview(proposal, requestId)
    },
  )

  async function handleApproveProposal(proposalId: string) {
    try {
      proposalActionBusyId.value = proposalId
      const updated = await automationApi.approveProposal(proposalId)
      proposals.value = proposals.value.map((p) => (p.id === proposalId ? updated : p))
      // APPROVED, not APPLIED (GH-1970): phase 1 of the two-phase decision has
      // landed and NOTHING has reached a board yet — the approve pane says as
      // much in the same breath, so the stamp must not contradict it.
      toast.success(t('review.toast.approved'), undefined, { label: 'approved' })
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, t('review.toast.approveFailed')).message)
    } finally {
      proposalActionBusyId.value = null
    }
  }

  async function handleRejectProposal(proposalId: string, riskLevel: ApiProposal['riskLevel']) {
    const requiresReason = ['High', 'Critical'].includes(normalizeProposalRiskLevel(riskLevel))
    const promptedReason = prompt(
      requiresReason
        ? t('review.prompt.rejectReasonRequired')
        : t('review.prompt.rejectReasonOptional'),
    )
    if (promptedReason === null) return

    const reason = promptedReason.trim()
    if (requiresReason && !reason) {
      toast.error(t('review.toast.rejectReasonRequired'))
      return
    }

    const reasonOrNull = reason.length > 0 ? reason : null

    try {
      proposalActionBusyId.value = proposalId
      const updated = await automationApi.rejectProposal(proposalId, reasonOrNull)
      proposals.value = proposals.value.map((p) => (p.id === proposalId ? updated : p))
      toast.success(t('review.toast.rejected'))
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, t('review.toast.rejectFailed')).message)
    } finally {
      proposalActionBusyId.value = null
    }
  }

  // Returns true only when the snooze actually persisted, so the caller can decide whether to
  // drop a deep-link hash. On failure the proposal is left untouched (a prior snooze stays in
  // effect), so the caller must NOT hide it.
  async function handleDeferProposal(proposalId: string): Promise<boolean> {
    try {
      proposalActionBusyId.value = proposalId
      const updated = await automationApi.deferProposal(proposalId)
      // Map the returned proposal in place so its new deferredUntil/expiresAt are live;
      // the ~60s review clock then resurfaces it when the snooze window elapses.
      proposals.value = proposals.value.map((p) => (p.id === proposalId ? updated : p))
      toast.success(t('review.toast.snoozed'))
      return true
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, t('review.toast.snoozeFailed')).message)
      return false
    } finally {
      proposalActionBusyId.value = null
    }
  }

  // --- Phase 2 (execute) confirmation --------------------------------------
  //
  // #1818: the approve→execute split is the ADR-0003 product invariant and does
  // NOT change here — only its feedback does. The second phase used to be gated
  // by a native `confirm()`, which cannot carry the proposal summary, is not
  // styled by either surface, and is invisible to component specs. It is now a
  // declarative request: the surface renders the app's own dialog (TdDialog, the
  // #1407 hardening idiom) bound to `executeConfirmProposalId`, and only
  // `confirmExecuteProposal()` reaches the API. `handleExecuteProposal` is
  // deliberately NOT exported so no caller can execute without that gate.
  const executeConfirmProposalId = ref<string | null>(null)

  /** The proposal awaiting the phase-2 confirmation, so the dialog can show its summary. */
  const executeConfirmProposal = computed<ApiProposal | null>(() => {
    const id = executeConfirmProposalId.value
    if (!id) return null
    return proposals.value.find((p) => p.id === id) ?? null
  })

  function requestExecuteProposal(proposalId: string) {
    // Another decision is mid-flight; opening the gate now would let the user
    // confirm against state that is already changing.
    if (proposalActionBusyId.value !== null) return
    executeConfirmProposalId.value = proposalId
  }

  function cancelExecuteProposal() {
    executeConfirmProposalId.value = null
  }

  async function confirmExecuteProposal() {
    const proposalId = executeConfirmProposalId.value
    if (!proposalId) return
    // Confirming against a proposal that vanished from the list (refresh, filter
    // change, dismissed elsewhere) would apply something no longer on screen —
    // and the dialog has already closed itself, since its `open` is derived from
    // this same computed. Load-bearing guard, not a tidy-up.
    const stillPresent = executeConfirmProposal.value !== null
    // Close the gate BEFORE awaiting so a double-confirm cannot fire two
    // executes; the Idempotency-Key would make the second a no-op server-side,
    // but the surface must not depend on that to stay honest.
    executeConfirmProposalId.value = null
    if (!stillPresent) return
    await handleExecuteProposal(proposalId)
  }

  // Keep the pending id from lingering after its proposal leaves the list, so a
  // later refresh that re-adds it cannot silently re-open the dialog. Sync flush:
  // a pre-flush watcher would miss a set-then-remove that happens in one tick.
  watch(
    executeConfirmProposal,
    (proposal) => {
      if (executeConfirmProposalId.value !== null && proposal === null) {
        executeConfirmProposalId.value = null
      }
    },
    { flush: 'sync' },
  )

  async function handleExecuteProposal(proposalId: string) {
    try {
      proposalActionBusyId.value = proposalId
      const updated = await automationApi.executeProposal(proposalId, createRequestId())
      proposals.value = proposals.value.map((p) => (p.id === proposalId ? updated : p))
      // The ONE path allowed to stamp APPLIED (GH-1970): phase 2 succeeded, so
      // the proposal really is written to the board. Every other success in the
      // app names its own outcome or falls back to a severity word.
      toast.success(t('review.toast.applied'), undefined, { label: 'applied' })
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, t('review.toast.applyFailed')).message)
    } finally {
      proposalActionBusyId.value = null
    }
  }

  async function handleToggleDiff(proposalId: string) {
    if (selectedDiffProposalId.value === proposalId) {
      latestDiffRequestId += 1
      resetDiffState()
      return
    }

    const proposal = proposals.value.find((p) => p.id === proposalId)
    // Anchor the pane to this proposal before any await so a concurrent toggle
    // or a stale response can be detected/ignored.
    const requestId = ++latestDiffRequestId
    selectedDiffProposalId.value = proposalId
    selectedDiff.value = null
    selectedDiffMode.value = null
    selectedDiffInvalidReason.value = null
    selectedDiffRevised.value = null

    if (!proposal) {
      // The card only emits ids from the visible list, but guard so a vanished
      // proposal leaves a clean pane rather than a hung loading state.
      resetDiffState()
      return
    }

    // Read-only / terminal proposals (expired, Applied, Rejected, Failed,
    // Dismissed) never fire the live diff — PR #1395 makes `/diff` 400 for them.
    // Present the stored `diffPreview` under an explicit read-only banner; when
    // there is no stored preview, the banner + "no stored preview" state, never
    // an error toast + cleared pane (#1397).
    if (isProposalReadOnly(proposal, isProposalExpired(proposal))) {
      presentStoredPreview(proposal, requestId)
      return
    }

    diffRenderPerf.start()
    try {
      const diff = await automationApi.getProposalDiff(proposalId)
      if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) return

      selectedDiff.value = diff
      selectedDiffMode.value = 'live'
    } catch (e: unknown) {
      if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) return

      // A 400 ValidationError means the backend ran Apply's gates at diff time
      // (#1376/#1395). It carries one of two distinct reasons — "Proposal must
      // contain at least one operation" or "Proposal has expired" (the expiry
      // race: the surface's 60s clock can lag a server-side expiry). Apply would
      // reject it for the SAME reason, so surface the backend's actual message
      // inline instead of a generic toast + torn-down pane (#1397 MEDIUM-1).
      if (isValidationError(e)) {
        selectedDiff.value = null
        selectedDiffMode.value = 'invalid'
        // Use the backend's ACTUAL reason, but treat a blank message as absent so
        // the card's specific "no operations" fallback copy applies rather than
        // the generic ValidationError string masking it (#1397 / #1414 review).
        selectedDiffInvalidReason.value = getValidationReason(e)
        return
      }

      // Any other failure (e.g. a 404 because it was deleted elsewhere) stays a
      // toast with a clean teardown.
      resetDiffState()
      toast.error(getErrorDisplay(e, t('review.toast.diffFailed')).message)
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
        toast.success(t('review.toast.dismissed'))
      } else {
        proposals.value = proposals.value.filter((p) => p.id !== proposalId)
        toast.info(t('review.toast.dismissedRefreshing'))
        void loadProposals()
      }
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, t('review.toast.dismissFailed')).message)
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
      toast.info(t('review.toast.nothingToClear'))
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
      toast.success(t('review.toast.cleared', { count: result.dismissed }, result.dismissed))
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, t('review.toast.clearFailed')).message)
    } finally {
      bulkDismissBusy.value = false
    }
  }

  return {
    proposalActionBusyId,
    bulkDismissBusy,
    selectedDiffProposalId,
    selectedDiff,
    selectedDiffMode,
    selectedDiffInvalidReason,
    selectedDiffRevised,
    executeConfirmProposalId,
    executeConfirmProposal,
    handleApproveProposal,
    handleRejectProposal,
    handleDeferProposal,
    requestExecuteProposal,
    cancelExecuteProposal,
    confirmExecuteProposal,
    handleToggleDiff,
    handleDismissProposal,
    handleDismissApplied,
  }
}
