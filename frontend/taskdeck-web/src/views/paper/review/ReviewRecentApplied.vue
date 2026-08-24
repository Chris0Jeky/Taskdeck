<script setup lang="ts">
/**
 * ReviewRecentApplied — factual recency ledger for settled proposals.
 * It records when an apply happened without implying a recovery action.
 */
export interface RecentlyAppliedRow {
  id: string
  serial: string
  title: string
  /** Pre-formatted age supplied by the parent (for example "30m"). */
  age: string
}

defineProps<{
  rows: RecentlyAppliedRow[]
  activeId?: string | null
}>()

const emit = defineEmits<{
  (event: 'select', id: string): void
}>()
</script>

<template>
  <div class="paper-review-recent">
    <div class="tk-eyebrow paper-review-recent__heading">{{ $t('review.recent.heading') }}</div>
    <div v-if="rows.length === 0" class="tk-meta paper-review-recent__empty">
      {{ $t('review.recent.empty') }}
    </div>
    <button
      v-for="row in rows"
      :key="row.id"
      type="button"
      class="paper-review-recent__row"
      :class="{ 'paper-review-recent__row--active': row.id === activeId }"
      :aria-label="$t('review.recent.openLabel', { title: row.title })"
      :aria-pressed="row.id === activeId"
      :data-proposal-id="row.id"
      @click="emit('select', row.id)"
    >
      <div class="paper-review-recent__head">
        <span class="tk-serial">{{ row.serial }}</span>
        <span class="tk-serial paper-review-recent__age">{{
          $t('review.recent.age', { age: row.age })
        }}</span>
      </div>
      <div class="paper-review-recent__title">{{ row.title }}</div>
    </button>
  </div>
</template>

<style scoped>
.paper-review-recent {
  padding: 12px 18px;
  border-top: 1px solid var(--line-soft);
  margin-top: 16px;
}
.paper-review-recent__heading {
  margin-bottom: 8px;
}
.paper-review-recent__empty {
  font-size: 10.5px;
}
.paper-review-recent__row {
  display: block;
  width: 100%;
  padding: 8px 0;
  background: transparent;
  border: 0;
  border-bottom: 1px solid var(--line-soft);
  font-family: inherit;
  font-size: 11.5px;
  color: var(--ink-2, var(--ink));
  cursor: pointer;
  text-align: left;
}
.paper-review-recent__row:hover,
.paper-review-recent__row--active {
  color: var(--ink);
  background: var(--paper-card);
}
.paper-review-recent__row:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 2px;
}
.paper-review-recent__head {
  display: flex;
  justify-content: space-between;
  margin-bottom: 2px;
}
.paper-review-recent__age {
  color: var(--faint);
}
.paper-review-recent__title {
  line-height: 1.35;
}
</style>
