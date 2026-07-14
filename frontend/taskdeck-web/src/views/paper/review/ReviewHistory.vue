<script setup lang="ts">
import type { HistoryRow } from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewHistory — § V: ledger table styled with `.rule-ledger`. Status
 * cells are coloured per state (pending = ember, applied = applied,
 * past = faint).
 */
defineProps<{ rows: HistoryRow[] }>()

function statusLabel(status: HistoryRow['status']): string {
  switch (status) {
    case 'pending':
      return 'PENDING'
    case 'applied':
      return 'APPLIED'
    case 'past':
      return 'past'
    case 'unknown':
      return 'UNKNOWN'
  }
}
</script>

<template>
  <section class="paper-review-history">
    <header class="paper-review-history__header">
      <span class="tk-serial paper-review-history__serial">§ V</span>
      <h3 class="tk-h3 paper-review-history__title">History · this card</h3>
      <span class="tk-meta paper-review-history__sub">Every touch since creation</span>
    </header>
    <div class="rule-ledger paper-review-history__ledger">
      <div v-if="rows.length === 0" class="tk-meta paper-review-history__empty">
        No history recorded.
      </div>
      <div
        v-for="row in rows"
        :key="`${row.serial}-${row.event}`"
        class="paper-review-history__row"
        :data-status="row.status"
      >
        <span class="tk-serial">{{ row.serial }}</span>
        <span class="paper-review-history__event">{{ row.event }}</span>
        <span class="paper-review-history__age">{{ row.age }}</span>
        <span class="paper-review-history__status">{{ statusLabel(row.status) }}</span>
      </div>
    </div>
  </section>
</template>

<style scoped>
.paper-review-history {
  margin-top: 28px;
}
.paper-review-history__header {
  display: flex;
  align-items: baseline;
  gap: 14px;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-review-history__serial {
  color: var(--faint);
}
.paper-review-history__title {
  margin: 0;
}
.paper-review-history__sub {
  margin-left: auto;
}
.paper-review-history__ledger {
  padding: 4px;
  border: 1px solid var(--line);
  border-radius: 2px;
  background: var(--paper-card);
}
.paper-review-history__empty {
  padding: 12px;
}
.paper-review-history__row {
  display: grid;
  grid-template-columns: 70px 1fr 80px 120px;
  padding: 5px 12px;
  font-family: var(--mono);
  font-size: 11px;
  align-items: center;
}
.paper-review-history__event {
  color: var(--ink-2, var(--ink));
}
.paper-review-history__age {
  text-align: right;
  color: var(--faint);
}
.paper-review-history__status {
  text-align: right;
  color: var(--faint);
  letter-spacing: 0.14em;
  text-transform: uppercase;
  font-size: 10px;
}
.paper-review-history__row[data-status='pending'] .paper-review-history__status {
  color: var(--ember);
}
.paper-review-history__row[data-status='applied'] .paper-review-history__status {
  color: var(--applied);
}
.paper-review-history__row[data-status='unknown'] .paper-review-history__status {
  color: var(--overdue);
  font-weight: 700;
}
</style>
