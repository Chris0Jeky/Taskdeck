import { computed, type ComputedRef } from 'vue'
import type { Proposal as ApiProposal } from '../types/automation'

/**
 * usePaperReviewSelectors — extends `useReviewProposals` output with the
 * Paper deep-Review selectors (provenance, side-effects, confidence
 * breakdown, conflicts, history, similar past).
 *
 * Backend gaps: as of 2026-04 the API does not yet expose these fields on
 * `Proposal`. Until the backend lands the corresponding shape, we feed the
 * surface from a feature-flagged demo stub so the Paper deep-Review view is
 * complete and reviewable. Each gap has an open follow-up issue (see
 * `paper-review-backend-gap-*` and references to PAPER-06 / #1002).
 *
 * The flag `PAPER_REVIEW_DEMO_FILL` defaults to `true`. When the backend
 * starts returning real data on the proposal payload, switch the selector to
 * read from there and flip the flag off (or wire to `featureFlagStore`).
 */

const PAPER_REVIEW_DEMO_FILL = true

export type ProvenanceWeight = 'primary' | 'contextual' | 'excluded' | 'inferred'

export interface ProvenanceRow {
  icon: string
  key: string
  value: string
  weight: ProvenanceWeight
}

export interface SideEffectRow {
  key: string
  value: string
  /** `active` rows are styled in serif italic with ember eyebrow. */
  tone: 'active' | 'passive'
}

export interface SideEffects {
  rows: SideEffectRow[]
  reversibility: {
    summary: string
    description: string
    /** Window in milliseconds for the undo timeline. Defaults to 6 h. */
    windowMs: number
    /** When the apply happened (ms epoch). For pending proposals we use now. */
    appliedAt: number
  }
}

export interface ConfidenceBreakdown {
  /** Aggregate value used by the dial (0..1). */
  overall: number
  /** Per-component bars; rendered in the right rail. */
  components: Array<{ key: string; value: number }>
  /** Footer note explaining a low component, when present. */
  note?: string
  /** Apply threshold from settings, used for the dial caption. */
  threshold: number
}

export interface ConflictRow {
  tone: 'warn' | 'info' | 'ok'
  key: string
  value: string
}

export interface HistoryRow {
  serial: string
  event: string
  age: string
  status: 'pending' | 'applied' | 'past'
}

export interface SimilarPastRow {
  serial: string
  title: string
  verdict: 'applied' | 'rejected'
  date: string
}

export interface PaperReviewSelectors {
  provenance: ComputedRef<ProvenanceRow[]>
  sideEffects: ComputedRef<SideEffects>
  confidenceBreakdown: ComputedRef<ConfidenceBreakdown>
  conflicts: ComputedRef<ConflictRow[]>
  history: ComputedRef<HistoryRow[]>
  similarPast: ComputedRef<SimilarPastRow[]>
  /** Aggregate apply rate of the similar-past list. */
  similarPastApplyRate: ComputedRef<{ applied: number; total: number; ratio: number }>
}

const EMPTY_PROVENANCE: ProvenanceRow[] = []
const EMPTY_CONFLICTS: ConflictRow[] = []
const EMPTY_HISTORY: HistoryRow[] = []
const EMPTY_SIMILAR: SimilarPastRow[] = []
const EMPTY_SIDE_EFFECTS: SideEffects = {
  rows: [],
  reversibility: {
    summary: '6 hours · single keystroke',
    description: 'Undo restores the prior state. Nothing is lost.',
    windowMs: 6 * 60 * 60 * 1000,
    appliedAt: Date.now(),
  },
}
const EMPTY_CONFIDENCE: ConfidenceBreakdown = {
  overall: 0,
  components: [],
  threshold: 0.7,
}

// TODO(#1002): Replace each demo block with backend-driven data once the
// gap-* issues land. The shape is stable; only the source changes.
const DEMO_PROVENANCE: ProvenanceRow[] = [
  {
    icon: '📄',
    key: 'card body',
    value: 'Card description · 178 words · last edited yesterday',
    weight: 'primary',
  },
  {
    icon: '🔗',
    key: 'design-doc',
    value: 'Dark Mode QA checklist · 5 items · attached last week',
    weight: 'primary',
  },
  {
    icon: '📜',
    key: 'board activity · 7 entries',
    value: 'Recent moves on this card and adjacent cards · last 14 days',
    weight: 'contextual',
  },
  {
    icon: '⊘',
    key: 'not read',
    value: 'Other boards · private cards · captures with different scope',
    weight: 'excluded',
  },
  {
    icon: '✦',
    key: 'inferred',
    value: 'Splitting threshold = 5+ subtasks OR >2 days estimated.',
    weight: 'inferred',
  },
]

const DEMO_SIDE_EFFECT_ROWS: SideEffectRow[] = [
  { key: 'Cards', value: '3 created · 1 archived (30 days)', tone: 'active' },
  { key: 'Subtasks', value: '8 distributed · none lost · checkmarks preserved', tone: 'active' },
  { key: 'Comments', value: 'Original 4 comments stay on the archived parent', tone: 'passive' },
  { key: 'Activity log', value: "Single entry: 'applied #014'", tone: 'active' },
  { key: 'Notifications', value: 'Author only · no team notify (solo board)', tone: 'passive' },
  { key: 'Webhooks', value: 'None · no integrations active on this board', tone: 'passive' },
  { key: 'Calendar', value: 'Untouched · due dates preserved or blank', tone: 'passive' },
]

const DEMO_CONFLICTS: ConflictRow[] = [
  {
    tone: 'warn',
    key: 'Stale assignment',
    value: 'Assignee was last active 9 days ago. Confirm before applying or reassign.',
  },
  {
    tone: 'info',
    key: 'Linked capture is older',
    value: 'Source capture is 2 days old. Still relevant?',
  },
  { tone: 'ok', key: 'No collisions', value: 'No other proposals touch this card right now.' },
]

const DEMO_HISTORY: HistoryRow[] = [
  { serial: '#014', event: 'haiku proposed split into 3', age: '11:42', status: 'pending' },
  { serial: '#011', event: 'subtask checked · audit AA', age: '09:18', status: 'applied' },
  { serial: '#009', event: "capture linked: 'Paper at Night QA'", age: 'yest 16:04', status: 'past' },
  { serial: '#007', event: 'body rewritten', age: 'yest 14:22', status: 'past' },
  { serial: '#003', event: 'label · theme added', age: 'Mon 11:00', status: 'past' },
  { serial: '#001', event: 'card created', age: 'wk 17 Mon', status: 'past' },
]

const DEMO_SIMILAR: SimilarPastRow[] = [
  { serial: '#984', title: "Split 'Auth flow' → 4 cards", verdict: 'applied', date: 'wk 14' },
  { serial: '#962', title: "Split 'Onboarding' → 3", verdict: 'rejected', date: 'wk 13' },
  { serial: '#941', title: 'Merge dupes (C-082, C-083)', verdict: 'applied', date: 'wk 12' },
]

const DEMO_CONFIDENCE: ConfidenceBreakdown = {
  overall: 0.84,
  components: [
    { key: 'Pattern match', value: 0.92 },
    { key: 'Reach', value: 0.88 },
    { key: 'Reversibility', value: 0.99 },
    { key: 'Recency · ctx', value: 0.61 },
  ],
  note: 'Lower-than-average on recency: source capture is 2 days old. Consider double-checking before apply.',
  threshold: 0.7,
}

export function usePaperReviewSelectors(
  activeProposal: ComputedRef<ApiProposal | null>,
): PaperReviewSelectors {
  const useDemo = computed(() => PAPER_REVIEW_DEMO_FILL && activeProposal.value !== null)

  const provenance = computed<ProvenanceRow[]>(() =>
    useDemo.value ? DEMO_PROVENANCE : EMPTY_PROVENANCE,
  )

  const sideEffects = computed<SideEffects>(() => {
    if (!useDemo.value) return EMPTY_SIDE_EFFECTS
    const proposal = activeProposal.value
    const appliedAt = proposal?.appliedAt ? new Date(proposal.appliedAt).getTime() : Date.now()
    return {
      rows: DEMO_SIDE_EFFECT_ROWS,
      reversibility: {
        summary: '6 hours · single keystroke',
        description:
          'Undo restores all affected cards to their prior state with original body, subtasks, comments, and activity log. Nothing is lost.',
        windowMs: 6 * 60 * 60 * 1000,
        appliedAt,
      },
    }
  })

  const confidenceBreakdown = computed<ConfidenceBreakdown>(() =>
    useDemo.value ? DEMO_CONFIDENCE : EMPTY_CONFIDENCE,
  )

  const conflicts = computed<ConflictRow[]>(() => (useDemo.value ? DEMO_CONFLICTS : EMPTY_CONFLICTS))

  const history = computed<HistoryRow[]>(() => (useDemo.value ? DEMO_HISTORY : EMPTY_HISTORY))

  const similarPast = computed<SimilarPastRow[]>(() =>
    useDemo.value ? DEMO_SIMILAR : EMPTY_SIMILAR,
  )

  const similarPastApplyRate = computed(() => {
    const rows = similarPast.value
    const applied = rows.filter((r) => r.verdict === 'applied').length
    const total = rows.length
    return { applied, total, ratio: total === 0 ? 0 : applied / total }
  })

  return {
    provenance,
    sideEffects,
    confidenceBreakdown,
    conflicts,
    history,
    similarPast,
    similarPastApplyRate,
  }
}
