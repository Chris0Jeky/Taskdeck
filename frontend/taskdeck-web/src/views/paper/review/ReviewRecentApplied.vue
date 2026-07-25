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
}>()
</script>

<template>
  <div class="paper-review-recent">
    <div class="tk-eyebrow paper-review-recent__heading">Recently applied</div>
    <div v-if="rows.length === 0" class="tk-meta paper-review-recent__empty">
      Nothing applied yet today.
    </div>
    <div
      v-for="row in rows"
      :key="row.id"
      class="paper-review-recent__row"
    >
      <div class="paper-review-recent__head">
        <span class="tk-serial">{{ row.serial }}</span>
        <span class="tk-serial paper-review-recent__age">{{ row.age }} ago</span>
      </div>
      <div class="paper-review-recent__title">{{ row.title }}</div>
    </div>
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
  padding: 8px 0;
  border-bottom: 1px solid var(--line-soft);
  font-size: 11.5px;
  color: var(--ink-2, var(--ink));
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
