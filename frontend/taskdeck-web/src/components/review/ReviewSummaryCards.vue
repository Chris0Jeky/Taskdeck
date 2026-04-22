<script setup lang="ts">
export type ReviewSummaryCard = {
  id: string
  label: string
  value: number
  helper: string
}

defineProps<{
  cards: ReviewSummaryCard[]
}>()
</script>

<template>
  <section class="td-review__summary" aria-label="Review statistics">
    <article v-for="card in cards" :key="card.id" class="td-panel td-review-summary-card">
      <span class="td-review-summary-card__value">{{ card.value }}</span>
      <span class="td-review-summary-card__label">{{ card.label }}</span>
      <span class="td-review-summary-card__helper">{{ card.helper }}</span>
    </article>
  </section>
</template>

<style scoped>
.td-review__summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: var(--td-space-3);
}

.td-review-summary-card {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.td-review-summary-card__value {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-2xl);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-review-summary-card__label {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-review-summary-card__helper {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

@media (max-width: 640px) {
  .td-review__summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: var(--td-space-2);
  }

  .td-review-summary-card {
    padding: var(--td-space-3);
  }

  .td-review-summary-card__value {
    font-size: var(--td-font-xl);
  }

  .td-review-summary-card__helper {
    display: none;
  }
}
</style>
