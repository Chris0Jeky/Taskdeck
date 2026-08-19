<script setup lang="ts">
import { computed } from 'vue'
import PaperKbd from '../../../components/paper/PaperKbd.vue'
import type { ApplyPhase } from './ReviewDecisionRail.vue'

/**
 * ReviewKeysCard — Decide-with-keys quick-reference rendered in the
 * ember-tint card. The shortcuts are static; behaviour is owned by
 * `useReviewKeymap`.
 *
 * ⏎ is the one row that is NOT static: it runs whichever half of the
 * ADR-0003 two-phase apply the active proposal is in, so its description
 * follows the phase (#1818 AC2).
 */
const props = withDefaults(defineProps<{ applyPhase?: ApplyPhase }>(), {
  applyPhase: 'approve',
})

const rows = computed<Array<{ key: string; label: string }>>(() => [
  {
    key: '⏎',
    label:
      props.applyPhase === 'execute'
        ? 'Confirm apply to board · step 2 of 2'
        : 'Approve proposal · step 1 of 2',
  },
  { key: 'E', label: 'Request edit · opens composer' },
  { key: '⌫', label: 'Reject · with optional reason' },
  { key: 'D', label: 'Defer 1h' },
  { key: 'P', label: 'Toggle provenance pane' },
  { key: 'space', label: 'Preview diff in card detail' },
])
</script>

<template>
  <section class="card paper-review-keys">
    <div class="tk-eyebrow paper-review-keys__eyebrow">Decide with keys</div>
    <div
      v-for="row in rows"
      :key="row.key"
      class="paper-review-keys__row"
    >
      <PaperKbd>{{ row.key }}</PaperKbd>
      <span class="paper-review-keys__label">{{ row.label }}</span>
    </div>
  </section>
</template>

<style scoped>
.paper-review-keys {
  padding: 14px;
  margin-top: 12px;
  border-color: var(--ember);
  background: var(--ember-tint);
}
.paper-review-keys__eyebrow {
  color: var(--ember-ink);
  margin-bottom: 6px;
}
.paper-review-keys__row {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 12px;
  color: var(--ember-ink);
  padding: 2px 0;
}
.paper-review-keys__label {
  flex: 1;
}
</style>
