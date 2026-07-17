import { ref, watch, type ComputedRef, type Ref } from 'vue'
import { automationApi } from '../api/automationApi'
import { proposalRevisionsApi } from '../api/proposalRevisionsApi'
import { useToastStore } from '../store/toastStore'
import { createRequestId } from '../utils/requestId'
import { normalizeProposalRiskLevel } from '../utils/automation'
import type { Proposal as ApiProposal } from '../types/automation'
import { getErrorDisplay, isValidationError } from './useErrorMapper'
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
// reactive clock. Trusts the server-authoritative `isExpired` flag and the
// domain `Expired` status; it cannot see a proposal whose expiresAt has passed
// mid-session, so surfaces that need that (Legacy) pass their own function.
function defaultIsProposalExpired(proposal: ApiProposal): boolean {
  return proposal.isExpired === true || proposal.status === 'Expired'
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

  function presentStoredPreview(proposal: ApiProposal, requestId: number) {
    selectedDiff.value = proposal.diffPreview
    selectedDiffMode.value = 'stored'
    selectedDiffInvalidReason.value = null
    selectedDiffRevised.value = null
    void loadStoredRevisedSignal(proposal.id, requestId)
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
      if (selectedDiffMode.value === 'stored' || selectedDiffMode.value === null) return
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
      toast.success('Snoozed for 1 hour — it will return to your queue.')
      return true
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to snooze proposal').message)
      return false
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
        selectedDiffInvalidReason.value = getErrorDisplay(
          e,
          'The backend rejected this proposal preview',
        ).message
        return
      }

      // Any other failure (e.g. a 404 because it was deleted elsewhere) stays a
      // toast with a clean teardown.
      resetDiffState()
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
    selectedDiffMode,
    selectedDiffInvalidReason,
    selectedDiffRevised,
    handleApproveProposal,
    handleRejectProposal,
    handleDeferProposal,
    handleExecuteProposal,
    handleToggleDiff,
    handleDismissProposal,
    handleDismissApplied,
  }
}
