<script setup lang="ts">
import { computed, ref } from 'vue'
import ReviewQueueItem from './ReviewQueueItem.vue'
import ReviewRecentApplied, { type RecentlyAppliedRow } from './ReviewRecentApplied.vue'
import ReviewMiniCadence from './ReviewMiniCadence.vue'

export interface QueueRailItem {
  id: string
  serial: string
  title: string
  who: string
  confidence: number | null
  age: string
  reach: string
  /** "mine" if the proposal belongs to the current user. */
  mine: boolean
  stale: boolean
}

export type QueueFilter = 'all' | 'mine' | 'stale'

const props = withDefaults(
  defineProps<{
    items: QueueRailItem[]
    activeId: string | null
    awaitingCount: number
    staleCount: number
    /** Caller-owned settled proposals on this board; ≥1 reveals the bulk file-away action. */
    dismissableCount?: number
    /** Disables the bulk file-away action while any review action is in flight. */
    busy?: boolean
    recentlyApplied: RecentlyAppliedRow[]
    cadence?: number[]
    applyRate?: number
    undoRate?: number
  }>(),
  {
    dismissableCount: 0,
    busy: false,
    cadence: () => [4, 3, 5, 2, 4, 1, 3],
    applyRate: 0.71,
    undoRate: 0.04,
  },
)

const emit = defineEmits<{
  (event: 'select', id: string): void
  (event: 'filter-change', filter: QueueFilter): void
  (event: 'file-away-all'): void
}>()

const filter = ref<QueueFilter>('all')

const visible = computed<QueueRailItem[]>(() => {
  switch (filter.value) {
    case 'mine':
      return props.items.filter((i) => i.mine)
    case 'stale':
      return props.items.filter((i) => i.stale)
    case 'all':
    default:
      return props.items
  }
})

function asPct(value: number): string {
  return `${Math.round(value * 100)}%`
}

function setFilter(next: QueueFilter) {
  filter.value = next
  emit('filter-change', next)
}
</script>

<template>
  <aside class="paper-review-rail" data-testid="paper-review-queue-rail">
    <div class="paper-review-rail__head">
      <div class="tk-eyebrow">
        Queue · {{ awaitingCount }} awaiting · {{ staleCount }} stale
      </div>
      <div class="paper-review-rail__filters" role="group" aria-label="Queue filters">
        <button
          v-for="key in (['all', 'mine', 'stale'] as QueueFilter[])"
          :key="key"
          type="button"
          class="paper-review-rail__pill"
          :class="{ 'paper-review-rail__pill--active': filter === key }"
          :aria-pressed="filter === key"
          @click="setFilter(key)"
        >{{ key === 'all' ? 'All' : key === 'mine' ? 'Mine' : 'Stale' }}</button>
      </div>
      <button
        v-if="dismissableCount >= 1"
        type="button"
        class="paper-review-rail__file-away"
        data-testid="queue-file-away-all"
        :disabled="busy"
        :aria-label="`File away ${dismissableCount} settled proposals`"
        @click="emit('file-away-all')"
      >File away {{ dismissableCount }} settled</button>
    </div>

    <div v-if="visible.length === 0" class="paper-review-rail__empty tk-meta">
      Nothing in this filter.
    </div>

    <ReviewQueueItem
      v-for="item in visible"
      :key="item.id"
      :serial="item.serial"
      :title="item.title"
      :who="item.who"
      :confidence="item.confidence"
      :age="item.age"
      :reach="item.reach"
      :active="item.id === activeId"
      :stale="item.stale"
      @select="emit('select', item.id)"
    />

    <ReviewRecentApplied :rows="recentlyApplied" />

    <div class="paper-review-rail__cadence">
      <div class="tk-eyebrow paper-review-rail__cadence-heading">This week</div>
      <ReviewMiniCadence :days="cadence" />
      <div class="tk-meta paper-review-rail__cadence-meta">
        Apply rate <b>{{ asPct(applyRate) }}</b> · undo rate <b>{{ asPct(undoRate) }}</b>
      </div>
    </div>
  </aside>
</template>

<style scoped>
.paper-review-rail {
  border-right: 1px solid var(--line);
  background: var(--paper-2);
  padding: 20px 0;
  overflow: auto;
  min-height: 0;
}
.paper-review-rail__head {
  padding: 0 18px 8px;
}
.paper-review-rail__filters {
  display: flex;
  gap: 6px;
  margin-top: 8px;
}
.paper-review-rail__pill {
  font-family: var(--mono);
  font-size: 10.5px;
  padding: 4px 8px;
  background: transparent;
  border: 1px solid var(--line-soft);
  color: var(--mute);
  cursor: pointer;
  letter-spacing: 0.04em;
}
.paper-review-rail__pill--active {
  background: var(--paper-card);
  color: var(--ink);
  border-color: var(--line);
}
.paper-review-rail__file-away {
  margin-top: 10px;
  width: 100%;
  font-family: var(--mono);
  font-size: 10.5px;
  letter-spacing: 0.04em;
  padding: 5px 8px;
  background: transparent;
  border: 1px dashed var(--line);
  color: var(--mute);
  cursor: pointer;
  text-align: left;
}
.paper-review-rail__file-away:hover:not(:disabled) {
  color: var(--ink);
  border-color: var(--ink);
}
.paper-review-rail__file-away:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.paper-review-rail__empty {
  padding: 14px 18px;
}
.paper-review-rail__cadence {
  margin-top: 8px;
  padding: 12px 18px;
  border-top: 1px solid var(--line-soft);
}
.paper-review-rail__cadence-heading {
  margin-bottom: 6px;
}
.paper-review-rail__cadence-meta {
  font-size: 10px;
  margin-top: 6px;
}
.paper-review-rail__cadence-meta b {
  color: var(--ink);
  font-weight: 500;
}
</style>
