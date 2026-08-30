import { computed, type ComputedRef, type Ref } from 'vue'
import type { Proposal as ApiProposal } from '../types/automation'

/**
 * Number of calendar days the review rail's mini-cadence covers, inclusive of
 * today. Seven keeps the "This week" heading honest.
 */
export const CADENCE_WINDOW_DAYS = 7

const MS_PER_DAY = 24 * 60 * 60 * 1000

/** Local midnight for the calendar day containing `ms`. */
function startOfLocalDay(ms: number): number {
  const d = new Date(ms)
  d.setHours(0, 0, 0, 0)
  return d.getTime()
}

export interface WeeklyCadenceOptions {
  /** The surface's reactive clock (`useReviewProposals().nowMs`). */
  nowMs: number
  /** Current session user id; cadence is per-user and needs it to attribute decisions. */
  userId: string | null | undefined
  /**
   * Optional board-scope predicate — pass `matchesActiveBoardFilter` so the
   * cadence agrees with the rest of the rail when a board filter is active.
   */
  includeBoard?: (boardId: string | null | undefined) => boolean
}

type WeeklyDecisionVisitor = (proposal: ApiProposal, dayDelta: number) => void

/**
 * Visit every decision in the rail's honest weekly cohort.
 *
 * Both cadence and Apply rate use this one predicate so attribution, local-day
 * boundaries, and board scope cannot drift between the two statistics.
 */
function visitWeeklyDecisions(
  proposals: readonly ApiProposal[] | null | undefined,
  options: WeeklyCadenceOptions,
  visit: WeeklyDecisionVisitor,
): number {
  const userId = options.userId
  if (!userId) return 0
  if (!Array.isArray(proposals) || proposals.length === 0) return 0

  const todayStart = startOfLocalDay(options.nowMs)
  if (Number.isNaN(todayStart)) return 0

  let decisions = 0
  for (const proposal of proposals) {
    if (!proposal) continue
    if (proposal.decidedByUserId !== userId) continue
    if (!proposal.decidedAt) continue
    if (options.includeBoard && !options.includeBoard(proposal.boardId)) continue

    const decidedMs = new Date(proposal.decidedAt).getTime()
    if (Number.isNaN(decidedMs)) continue

    // Round the day delta rather than flooring it: DST transitions make a
    // calendar day 23 or 25 hours long, and flooring would shift a bar by one
    // column across the boundary.
    const dayDelta = Math.round((todayStart - startOfLocalDay(decidedMs)) / MS_PER_DAY)
    // Drop anything outside the window, including future-dated decisions from a
    // skewed clock — they are not "this week" and must not be folded into today.
    if (dayDelta < 0 || dayDelta >= CADENCE_WINDOW_DAYS) continue

    visit(proposal, dayDelta)
    decisions += 1
  }

  return decisions
}

/**
 * Real per-day counts of proposals **decided by the current user** over the last
 * {@link CADENCE_WINDOW_DAYS} calendar days, oldest → newest (last entry = today).
 *
 * Source of truth is the review-queue payload the surface already loads
 * (`GET /automation/proposals`), whose `ProposalDto` carries `decidedAt` and
 * `decidedByUserId`. No new API surface is introduced (GP-03): this is a pure
 * projection of data the rail already has in hand.
 *
 * Returns `undefined` — never a fabricated array — whenever an honest count is
 * unavailable or empty:
 *   - no session user id (a per-user figure cannot be attributed),
 *   - no decision by this user inside the window (a flat all-zero strip would
 *     read as "activity" while carrying none).
 * `ReviewMiniCadence` hides itself on `undefined`, which is the no-fabrication
 * contract pinned by the #1796 specs.
 *
 * Known bound, deliberately not papered over: `loadProposals()` fetches at most
 * 200 proposals, so on an extremely high-volume account a day's count is a
 * floor rather than a total. Every rendered bar is still a real decision count;
 * nothing is invented. Raising that ceiling is a review-queue paging concern,
 * not a cadence one.
 */
export function buildWeeklyCadence(
  proposals: readonly ApiProposal[] | null | undefined,
  options: WeeklyCadenceOptions,
): number[] | undefined {
  const buckets = new Array<number>(CADENCE_WINDOW_DAYS).fill(0)
  const decisions = visitWeeklyDecisions(proposals, options, (_proposal, dayDelta) => {
    buckets[CADENCE_WINDOW_DAYS - 1 - dayDelta] += 1
  })

  return decisions > 0 ? buckets : undefined
}

/**
 * Portion of the rail's weekly decided cohort that reached Apply.
 *
 * `appliedAt` is the lifecycle authority, not the proposal's current status: a
 * proposal may be dismissed after it was applied and must remain in the
 * numerator. `undefined` means there is no attributable cohort; numeric zero
 * means the user made decisions but none reached Apply.
 */
export function buildWeeklyApplyRate(
  proposals: readonly ApiProposal[] | null | undefined,
  options: WeeklyCadenceOptions,
): number | undefined {
  let applied = 0
  const decisions = visitWeeklyDecisions(proposals, options, (proposal) => {
    if (typeof proposal.appliedAt === 'string') applied += 1
  })

  return decisions > 0 ? applied / decisions : undefined
}

/**
 * Reactive wrapper over {@link buildWeeklyCadence} for the Paper review surface.
 * Yields `undefined` whenever there is no honest cadence to show.
 */
export function useReviewCadence(
  proposals: Ref<ApiProposal[]>,
  nowMs: Ref<number>,
  userId: Ref<string | null> | (() => string | null | undefined),
  includeBoard?: (boardId: string | null | undefined) => boolean,
): ComputedRef<number[] | undefined> {
  return computed(() =>
    buildWeeklyCadence(proposals.value, {
      nowMs: nowMs.value,
      userId: typeof userId === 'function' ? userId() : userId.value,
      includeBoard,
    }),
  )
}

/** Reactive wrapper over {@link buildWeeklyApplyRate} for the Paper review rail. */
export function useReviewApplyRate(
  proposals: Ref<ApiProposal[]>,
  nowMs: Ref<number>,
  userId: Ref<string | null> | (() => string | null | undefined),
  includeBoard?: (boardId: string | null | undefined) => boolean,
): ComputedRef<number | undefined> {
  return computed(() =>
    buildWeeklyApplyRate(proposals.value, {
      nowMs: nowMs.value,
      userId: typeof userId === 'function' ? userId() : userId.value,
      includeBoard,
    }),
  )
}
