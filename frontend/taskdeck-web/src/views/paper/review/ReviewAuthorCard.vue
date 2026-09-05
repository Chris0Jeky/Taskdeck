<script setup lang="ts">
import { computed, ref } from 'vue'
import PaperStamp from '../../../components/paper/PaperStamp.vue'
import type { ConfidenceBreakdown } from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewAuthorCard — author badge with absolute-positioned PaperStamp
 * (rotated −9°) plus per-component confidence bars.
 *
 * Keep `pointer-events: none` on the stamp so the rotation stays decorative
 * and never intercepts clicks on the card.
 *
 * The confidence-SOURCE sentence is card-level, never disclosure-only (#1940).
 * On an Applied record ReviewMain gates its confidence-source badge off and the
 * view passes an empty `authorMeta` for the deterministic and not-reported
 * sources, which left this sentence as the only statement on screen about where
 * the number came from — hidden behind a control with no reason to be opened.
 *
 * KNOWN GAP, unfixable at this layer (#1940), mirroring ReviewSimilarPast. The
 * card receives a breakdown and nothing about the fetch that produced it, and
 * `usePaperReviewSelectors` initialises `confidenceData` to `EMPTY_CONFIDENCE`
 * — zero components, source `not-reported`, no note — resets it there on every
 * proposal switch, and LEAVES it there when the batch fails, with no flag
 * (`evidenceUnavailable` is set only from the Apply-time refresh, never from
 * the page-load batch). So "No model confidence reported" also renders while
 * the read is in flight and after it failed, where it is a claim about a
 * response that never arrived. The composable exposes `loading`, but nothing
 * threads it into `ReviewRightRail`, and the only place that could is
 * `PaperReviewView.vue`.
 *
 * The wrong-state copy predates #1940: the same sentence rendered inside the
 * disclosure. Hoisting it makes an existing false claim easier to see rather
 * than creating one, and the gap stays tracked on #1940.
 */
const props = defineProps<{
  authorName: string
  authorMeta: string
  proposedDate: string
  proposedTime: string
  proposedNum: string
  breakdown: ConfidenceBreakdown
}>()

const confidenceDetailsExpanded = ref(false)
const confidenceDetailsId = 'paper-review-confidence-details'
const confidenceDisclosureId = 'paper-review-confidence-disclosure'

/** No per-component bars to show, so the source sentence is all there is. */
const noComponents = computed(() => props.breakdown.components.length === 0)

/**
 * The heading is derived from what is actually rendered, not from the claimed
 * source: `model-reported` with an empty components array would otherwise
 * announce "Model-reported item confidence" over a body stating that no model
 * confidence was reported. The backend does not emit that pair today (only a
 * view-spec fixture builds it), but deriving the heading means it cannot be
 * constructed at all.
 */
const confidenceHeadingKey = computed(() =>
  props.breakdown.source === 'model-reported' && !noComponents.value
    ? 'review.author.modelReportedHeading'
    : 'review.author.confidenceHeading',
)

function barColor(value: number): string {
  if (value > 0.8) return 'var(--applied)'
  if (value > 0.6) return 'var(--ember)'
  return 'var(--overdue)'
}
</script>

<template>
  <section class="card paper-review-author">
    <PaperStamp
      class="paper-review-author__stamp"
      kind="proposed"
      :date="proposedDate"
      :time="proposedTime"
      :num="proposedNum"
      :rotate="-9"
    />
    <div class="tk-eyebrow paper-review-author__eyebrow">{{ $t('review.author.heading') }}</div>
    <div class="paper-review-author__row">
      <span class="paper-review-author__bullet" aria-hidden="true">✦</span>
      <div>
        <div class="paper-review-author__name">{{ authorName }}</div>
        <div v-if="authorMeta" class="tk-meta paper-review-author__meta">{{ authorMeta }}</div>
      </div>
    </div>
    <p
      v-if="noComponents"
      class="paper-review-author__empty tk-meta"
      data-testid="paper-review-author-confidence-source"
    >
      {{
        breakdown.source === 'deterministic'
          ? $t('review.author.deterministic')
          : $t('review.author.notReported')
      }}
    </p>
    <button
      :id="confidenceDisclosureId"
      type="button"
      class="paper-review-author__disclosure"
      data-testid="paper-review-confidence-disclosure"
      :aria-controls="confidenceDetailsId"
      :aria-expanded="confidenceDetailsExpanded"
      @click="confidenceDetailsExpanded = !confidenceDetailsExpanded"
    >
      <span>{{
        confidenceDetailsExpanded
          ? $t('review.author.details.hide')
          : $t('review.author.details.show')
      }}</span>
      <span aria-hidden="true">{{ confidenceDetailsExpanded ? '−' : '+' }}</span>
    </button>
    <div
      v-show="confidenceDetailsExpanded"
      :id="confidenceDetailsId"
      class="paper-review-author__details"
      data-testid="paper-review-confidence-details"
      role="region"
      :aria-labelledby="confidenceDisclosureId"
      :hidden="!confidenceDetailsExpanded"
    >
      <hr class="hr-soft paper-review-author__rule" />
      <div class="tk-eyebrow paper-review-author__bd-heading">
        {{ $t(confidenceHeadingKey) }}
      </div>
      <div
        v-for="component in breakdown.components"
        :key="component.key"
        class="paper-review-author__bar"
      >
        <span class="paper-review-author__bar-key">{{ component.key }}</span>
        <div class="paper-review-author__bar-track">
          <div
            class="paper-review-author__bar-fill"
            :style="{ width: `${Math.min(1, Math.max(0, component.value)) * 100}%`, background: barColor(component.value) }"
          />
        </div>
        <span class="tk-serial paper-review-author__bar-value">{{ component.value.toFixed(2) }}</span>
      </div>
      <template v-if="breakdown.note">
        <hr class="hr-soft paper-review-author__rule" />
        <p class="tk-meta paper-review-author__note">{{ breakdown.note }}</p>
      </template>
    </div>
  </section>
</template>

<style scoped>
.paper-review-author {
  padding: 14px;
  position: relative;
}
.paper-review-author__stamp {
  position: absolute;
  right: -6px;
  top: -10px;
  pointer-events: none;
}
.paper-review-author__eyebrow {
  margin-bottom: 8px;
}
.paper-review-author__row {
  display: flex;
  align-items: center;
  gap: 10px;
}
.paper-review-author__bullet {
  color: var(--ember);
  font-size: 16px;
  line-height: 1;
}
.paper-review-author__name {
  font-weight: 500;
  font-size: 13px;
  color: var(--ink-deep);
}
.paper-review-author__meta {
  font-size: 10px;
}
.paper-review-author__disclosure {
  width: 100%;
  min-height: 40px;
  margin-top: 10px;
  padding: 8px 0 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border: 0;
  border-top: 1px solid var(--line-soft);
  background: transparent;
  color: var(--ember);
  font: inherit;
  font-size: 11px;
  font-weight: 600;
  text-align: left;
  cursor: pointer;
}
.paper-review-author__disclosure:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 3px;
}
.paper-review-author__rule {
  margin: 10px 0;
}
.paper-review-author__bd-heading {
  margin-bottom: 4px;
}
.paper-review-author__bar {
  display: grid;
  grid-template-columns: 1fr 80px 28px;
  gap: 8px;
  align-items: center;
  margin-bottom: 4px;
}
.paper-review-author__bar-key {
  font-size: 11px;
  color: var(--ink-2, var(--ink));
}
.paper-review-author__bar-track {
  height: 4px;
  background: var(--paper-2);
  border: 1px solid var(--line-soft);
  position: relative;
}
.paper-review-author__bar-fill {
  position: absolute;
  inset: 0 auto 0 0;
  background: var(--ember);
}
.paper-review-author__bar-value {
  text-align: right;
}
.paper-review-author__note {
  margin: 0;
  font-size: 10.5px;
  line-height: 1.5;
}
.paper-review-author__empty {
  margin: 6px 0 0;
  line-height: 1.45;
}
</style>
