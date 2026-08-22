import { computed, type ComputedRef, type Ref } from 'vue'
import type { CaptureItemSummary, CaptureStatusValue } from '../types/capture'

/**
 * The two inbox counters, from one definition (#1974).
 *
 * Two numbers were on screen at once, both presented as "the queue", and they
 * disagreed: the sidebar badge showed the server's pending count while the
 * Inbox header showed `items.length` — every capture fetched, applied ones
 * included. A user cannot tell which number means "needs my attention" when
 * both are called a queue and differ by three.
 *
 * The definition kept is the SERVER's, because the sidebar badge and Home's
 * triage line already use it: `WorkspaceService` computes
 * `capturesNeedingTriage` as `NewCount + FailedCount`, so a capture is pending
 * while it is untouched (`New`) or failed and retryable (`Failed`), and is not
 * pending once it is triaging, triaged, proposed, converted or ignored.
 * `isPendingTriageStatus` is that rule mirrored client-side; changing one side
 * without the other is what put the two counters three apart.
 *
 * `capturedCount` stays available as a SEPARATE, separately-labelled number —
 * the surface may show both, but never presents the total as a queue.
 *
 * Scope note: these count the rows the caller passes in. The Inbox list is
 * fetched with a limit and can be board-scoped, so this is the count of what
 * is on screen, while the sidebar badge is workspace-wide. That is why the two
 * are labelled distinctly rather than asserted to be the same integer.
 *
 * Statuses arrive as either the enum name or its ordinal depending on the
 * serializer in play, so both forms are listed — as `TRIAGE_TERMINAL_STATUSES`
 * in `types/capture.ts` already does for its own predicate.
 */
const PENDING_TRIAGE_STATUSES: readonly CaptureStatusValue[] = ['New', 0, 'Failed', 6]

export function isPendingTriageStatus(status: CaptureStatusValue): boolean {
  return PENDING_TRIAGE_STATUSES.includes(status)
}

export function useInboxCounts(
  items: Ref<CaptureItemSummary[]> | ComputedRef<CaptureItemSummary[]>,
) {
  const pendingTriageCount = computed(
    () => items.value.filter((item) => isPendingTriageStatus(item.status)).length,
  )
  const capturedCount = computed(() => items.value.length)

  return { pendingTriageCount, capturedCount }
}
