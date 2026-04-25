<script setup lang="ts">
/**
 * ReviewRecentApplied — "Recently applied · undoable" stack with
 * countdown timers. Each row shows ↶ left or "sealed" once the window
 * closed. The countdown text is supplied by the parent (formatted on
 * the same clock that drives the queue's now()).
 */
export interface RecentlyAppliedRow {
  serial: string
  title: string
  /** Pre-formatted "5h 48m" string, or null when expired. */
  left: string | null
  expired: boolean
}

defineProps<{
  rows: RecentlyAppliedRow[]
}>()
</script>

<template>
  <div class="paper-review-recent">
    <div class="tk-eyebrow paper-review-recent__heading">Recently applied · undoable</div>
    <div v-if="rows.length === 0" class="tk-meta paper-review-recent__empty">
      Nothing applied yet today.
    </div>
    <div
      v-for="row in rows"
      :key="row.serial"
      class="paper-review-recent__row"
      :data-expired="row.expired ? 'true' : null"
    >
      <div class="paper-review-recent__head">
        <span class="tk-serial">{{ row.serial }}</span>
        <span
          v-if="!row.expired"
          class="tk-serial paper-review-recent__left"
        >↶ {{ row.left ?? '—' }}</span>
        <span v-else class="tk-serial paper-review-recent__sealed">sealed</span>
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
.paper-review-recent__row[data-expired='true'] {
  color: var(--faint);
}
.paper-review-recent__head {
  display: flex;
  justify-content: space-between;
  margin-bottom: 2px;
}
.paper-review-recent__left {
  color: var(--ember);
}
.paper-review-recent__sealed {
  color: var(--faint);
}
.paper-review-recent__title {
  line-height: 1.35;
}
</style>
