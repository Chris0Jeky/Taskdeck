<script setup lang="ts">
import ReviewAuthorCard from './ReviewAuthorCard.vue'
import ReviewWhyNow from './ReviewWhyNow.vue'
import ReviewSimilarPast from './ReviewSimilarPast.vue'
import ReviewKeysCard from './ReviewKeysCard.vue'
import type { ApplyPhase } from './ReviewDecisionRail.vue'
import type {
  ConfidenceBreakdown,
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
  }>(),
  { applyPhase: 'approve', applyOnly: false, receiptActive: false },
)
</script>

<template>
  <aside class="paper-review-right" data-testid="paper-review-right-rail">
    <ReviewAuthorCard
      :author-name="authorName"
      :author-meta="authorMeta"
      :proposed-date="proposedDate"
      :proposed-time="proposedTime"
      :proposed-num="proposedNum"
      :breakdown="breakdown"
    />
    <ReviewWhyNow :body="whyNowBody" />
    <ReviewSimilarPast :rows="similarPast" :apply-rate="similarPastApplyRate" />
    <ReviewKeysCard
      v-if="!receiptActive || applyOnly"
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
