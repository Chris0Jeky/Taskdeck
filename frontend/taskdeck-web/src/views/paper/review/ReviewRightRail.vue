<script setup lang="ts">
import ReviewAuthorCard from './ReviewAuthorCard.vue'
import ReviewWhyNow from './ReviewWhyNow.vue'
import ReviewSimilarPast from './ReviewSimilarPast.vue'
import ReviewKeysCard from './ReviewKeysCard.vue'
import type { ApplyPhase } from './ReviewDecisionRail.vue'
import type {
  ConfidenceBreakdown,
  PaperReviewEvidenceStatus,
  SimilarPastRow,
} from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewRightRail — the 320 px right column: author card with stamp,
 * why-now, similar-past, decide-with-keys.
 */
withDefaults(
  defineProps<{
    authorName: string
    authorMeta: string
    proposedDate: string
    proposedTime: string
    proposedNum: string
    whyNowBody: string
    breakdown: ConfidenceBreakdown
    similarPast: SimilarPastRow[]
    similarPastApplyRate: { applied: number; total: number; ratio: number }
    /** Passed through so the ⏎ row names the phase it will actually run (#1818). */
    applyPhase?: ApplyPhase
    /** An approved receipt has no remaining reject/edit/defer choices. */
    applyOnly?: boolean
    /** A terminal/deferred receipt has no keyboard decision affordances. */
    receiptActive?: boolean
    /** Historical Applied records never expose live decision key hints. */
    appliedRecord?: boolean
    /** Required evidence could not be refreshed for the active saved revision. */
    evidenceUnavailable?: boolean
    /**
     * State of the core evidence batch `breakdown` and `similarPast` came from,
     * forwarded verbatim to the two cards that state something about them
     * (#1940). The default is the CONSERVATIVE one: a caller that does not know
     * the state gets cards that withhold their claims rather than cards that
     * assert emptiness on values whose read may still be running.
     */
    evidenceState?: PaperReviewEvidenceStatus
  }>(),
  {
    applyPhase: 'approve',
    applyOnly: false,
    receiptActive: false,
    appliedRecord: false,
    evidenceUnavailable: false,
    evidenceState: 'loading',
  },
)
</script>

<template>
  <aside class="paper-review-right" data-testid="paper-review-right-rail">
    <ReviewAuthorCard
      v-if="!evidenceUnavailable"
      :author-name="authorName"
      :author-meta="authorMeta"
      :proposed-date="proposedDate"
      :proposed-time="proposedTime"
      :proposed-num="proposedNum"
      :breakdown="breakdown"
      :evidence-state="evidenceState"
    />
    <ReviewWhyNow :body="whyNowBody" />
    <ReviewSimilarPast
      v-if="!evidenceUnavailable"
      :rows="similarPast"
      :apply-rate="similarPastApplyRate"
      :evidence-state="evidenceState"
    />
    <ReviewKeysCard
      v-if="!appliedRecord && (!receiptActive || applyOnly)"
      :apply-phase="applyPhase"
      :apply-only="applyOnly"
    />
  </aside>
</template>

<style scoped>
.paper-review-right {
  border-left: 1px solid var(--line);
  background: var(--paper-2);
  padding: 20px 18px;
  overflow: auto;
  min-height: 0;
}
</style>
