<script setup lang="ts">
import PaperStamp from '../../../components/paper/PaperStamp.vue'
import type { ConfidenceBreakdown } from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewAuthorCard — author badge with absolute-positioned PaperStamp
 * (rotated −9°) plus per-component confidence bars.
 *
 * Keep `pointer-events: none` on the stamp so the rotation stays decorative
 * and never intercepts clicks on the card.
 */
defineProps<{
  authorName: string
  authorMeta: string
  proposedDate: string
  proposedTime: string
  proposedNum: string
  breakdown: ConfidenceBreakdown
}>()

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
    <hr class="hr-soft paper-review-author__rule" />
    <div class="tk-eyebrow paper-review-author__bd-heading">
      {{
        breakdown.source === 'model-reported'
          ? $t('review.author.modelReportedHeading')
          : $t('review.author.confidenceHeading')
      }}
    </div>
    <p
      v-if="breakdown.components.length === 0"
      class="paper-review-author__empty tk-meta"
      data-testid="paper-review-author-confidence-source"
    >
      {{
        breakdown.source === 'deterministic'
          ? $t('review.author.deterministic')
          : $t('review.author.notReported')
      }}
    </p>
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
