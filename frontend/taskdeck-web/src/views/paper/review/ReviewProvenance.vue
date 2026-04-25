<script setup lang="ts">
import { computed } from 'vue'
import type { ProvenanceRow, ProvenanceWeight } from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewProvenance — § II: 5-row table of what haiku read / didn't /
 * inferred. Each row gets a 32 px icon + 200 px italic key + flex value.
 * Data shape comes from `usePaperReviewSelectors`.
 */
const props = defineProps<{ rows: ProvenanceRow[] }>()

const empty = computed(() => props.rows.length === 0)

function tone(weight: ProvenanceWeight): string {
  switch (weight) {
    case 'primary':
      return 'var(--ink)'
    case 'excluded':
      return 'var(--faint)'
    case 'inferred':
      return 'var(--ember)'
    case 'contextual':
    default:
      return 'var(--ink-2, var(--ink))'
  }
}
</script>

<template>
  <section class="paper-review-prov">
    <header class="paper-review-prov__header">
      <span class="tk-serial paper-review-prov__serial">§ II</span>
      <h3 class="tk-h3 paper-review-prov__title">Provenance</h3>
      <span class="tk-meta paper-review-prov__sub">
        What haiku read · what it didn't · what it inferred
      </span>
    </header>
    <div class="card paper-review-prov__card">
      <div v-if="empty" class="paper-review-prov__empty tk-meta">
        Provenance not available for this proposal yet.
      </div>
      <div
        v-for="row in rows"
        :key="`${row.weight}:${row.key}`"
        class="paper-review-prov__row"
      >
        <span class="paper-review-prov__icon" :style="{ color: tone(row.weight) }">{{ row.icon }}</span>
        <span class="paper-review-prov__key" :style="{ color: tone(row.weight) }">{{ row.key }}</span>
        <span class="paper-review-prov__value">{{ row.value }}</span>
      </div>
    </div>
    <p class="tk-meta paper-review-prov__footnote">
      Haiku ran <b>locally</b>. No data left this device.
      <a href="#" class="paper-review-prov__more">View full read-set →</a>
    </p>
  </section>
</template>

<style scoped>
.paper-review-prov {
  margin-top: 28px;
}
.paper-review-prov__header {
  display: flex;
  align-items: baseline;
  gap: 14px;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-review-prov__serial {
  color: var(--faint);
}
.paper-review-prov__title {
  margin: 0;
}
.paper-review-prov__sub {
  margin-left: auto;
}
.paper-review-prov__card {
  padding: 0;
  overflow: hidden;
}
.paper-review-prov__empty {
  padding: 16px;
}
.paper-review-prov__row {
  display: grid;
  grid-template-columns: 32px 200px 1fr;
  gap: 12px;
  padding: 11px 16px;
  border-bottom: 1px solid var(--line-soft);
  align-items: flex-start;
}
.paper-review-prov__row:last-child {
  border-bottom: 0;
}
.paper-review-prov__icon {
  font-size: 14px;
  line-height: 1.3;
}
.paper-review-prov__key {
  font-family: var(--serif);
  font-style: italic;
  font-size: 13px;
}
.paper-review-prov__value {
  font-size: 12.5px;
  color: var(--ink-2, var(--ink));
}
.paper-review-prov__footnote {
  margin-top: 8px;
  font-size: 11px;
}
.paper-review-prov__footnote b {
  color: var(--ink);
  font-weight: 500;
}
.paper-review-prov__more {
  color: var(--ember);
  border-bottom: 1px solid var(--ember);
  text-decoration: none;
  margin-left: 4px;
}
</style>
