<script setup lang="ts">
import { computed } from 'vue'
import type { ConflictRow } from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewConflicts — § IV: warn rust / info mute / ok sage rows. The
 * `tone="warn"` colour is `--overdue` per the styleguide so ember stays
 * reserved for proposal moments.
 */
const props = defineProps<{ rows: ConflictRow[] }>()

const warnCount = computed(() => props.rows.filter((r) => r.tone === 'warn').length)
const subTitle = computed(() =>
  warnCount.value === 0
    ? 'What the system noticed · clear'
    : `What the system noticed · ${warnCount.value} ${warnCount.value === 1 ? 'minor' : 'items'}`,
)

function color(tone: ConflictRow['tone']): string {
  switch (tone) {
    case 'warn':
      return 'var(--overdue)'
    case 'ok':
      return 'var(--applied)'
    case 'info':
    default:
      return 'var(--mute)'
  }
}
function glyph(tone: ConflictRow['tone']): string {
  switch (tone) {
    case 'warn':
      return '‼'
    case 'ok':
      return '✓'
    case 'info':
    default:
      return '·'
  }
}
function label(tone: ConflictRow['tone']): string {
  switch (tone) {
    case 'warn':
      return 'WARNING'
    case 'ok':
      return 'CLEAR'
    case 'info':
    default:
      return 'INFO'
  }
}
</script>

<template>
  <section class="paper-review-conflicts">
    <header class="paper-review-conflicts__header">
      <span class="tk-serial paper-review-conflicts__serial">§ IV</span>
      <h3 class="tk-h3 paper-review-conflicts__title">Conflicts &amp; warnings</h3>
      <span class="tk-meta paper-review-conflicts__sub">{{ subTitle }}</span>
    </header>
    <div class="card paper-review-conflicts__card">
      <div v-if="rows.length === 0" class="paper-review-conflicts__empty tk-meta">
        Nothing flagged.
      </div>
      <div
        v-for="row in rows"
        :key="`${row.tone}:${row.key}`"
        class="paper-review-conflicts__row"
      >
        <span class="paper-review-conflicts__glyph" :style="{ color: color(row.tone) }">
          {{ glyph(row.tone) }}
        </span>
        <span
          class="tagstamp paper-review-conflicts__tag"
          :style="{ color: color(row.tone) }"
        >{{ label(row.tone) }}</span>
        <div>
          <div class="paper-review-conflicts__key">{{ row.key }}</div>
          <p class="paper-review-conflicts__value">{{ row.value }}</p>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.paper-review-conflicts {
  margin-top: 28px;
}
.paper-review-conflicts__header {
  display: flex;
  align-items: baseline;
  gap: 14px;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-review-conflicts__serial {
  color: var(--faint);
}
.paper-review-conflicts__title {
  margin: 0;
}
.paper-review-conflicts__sub {
  margin-left: auto;
}
.paper-review-conflicts__card {
  padding: 0;
  overflow: hidden;
}
.paper-review-conflicts__empty {
  padding: 16px;
}
.paper-review-conflicts__row {
  display: grid;
  grid-template-columns: 32px 200px 1fr;
  gap: 12px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--line-soft);
  align-items: center;
}
.paper-review-conflicts__row:last-child {
  border-bottom: 0;
}
.paper-review-conflicts__glyph {
  font-family: var(--serif);
  font-size: 18px;
  text-align: center;
}
.paper-review-conflicts__tag {
  width: fit-content;
}
.paper-review-conflicts__key {
  font-family: var(--serif);
  font-size: 14px;
  font-weight: 500;
  color: var(--ink-deep);
}
.paper-review-conflicts__value {
  margin: 2px 0 0;
  font-size: 12.5px;
  color: var(--ink-2, var(--ink));
}
</style>
