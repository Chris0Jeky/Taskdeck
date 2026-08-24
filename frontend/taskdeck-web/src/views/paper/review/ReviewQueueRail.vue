<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import ReviewQueueItem from './ReviewQueueItem.vue'
import ReviewRecentApplied, { type RecentlyAppliedRow } from './ReviewRecentApplied.vue'
import ReviewMiniCadence from './ReviewMiniCadence.vue'
import PaperScopeDisclosure from '../../../components/paper/PaperScopeDisclosure.vue'

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
    scopeLabel?: string
    scopeClearLabel?: string
    /** Caller-owned settled proposals on this board; ≥1 reveals the bulk file-away action. */
    dismissableCount?: number
    /** Disables the bulk file-away action while any review action is in flight. */
    busy?: boolean
    recentlyApplied: RecentlyAppliedRow[]
    /**
     * Real 7-day activity counts (oldest → newest). Omit when there is no
     * decision history — the mini-cadence bars are hidden rather than invented.
     */
    cadence?: number[]
    /**
     * Real apply rate (0–1) derived from decision history. Omit when there is
     * no decision history so the rail shows an honest empty state instead of a
     * fabricated percentage. There is intentionally no default value.
     */
    applyRate?: number
    /**
     * Whether the author partition — the "All" vs "Mine" split — can mean
     * anything for this viewer. It comes from the server-computed
     * collaboration-membership contract, never from proposal authorship,
     * board ACL rows alone, or online presence (#1940).
     *
     * Defaults to `true` so a loading, unknown, or failed membership lookup
     * keeps every control on screen; the pair is removed only from a positive
     * single-member answer. Stale is never removed.
     */
    authorPartitionAvailable?: boolean
  }>(),
  {
    dismissableCount: 0,
    busy: false,
    authorPartitionAvailable: true,
  },
)

const emit = defineEmits<{
  (event: 'select', id: string): void
  (event: 'filter-change', filter: QueueFilter): void
  (event: 'file-away-all'): void
  (event: 'clear-scope'): void
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

/** Real 7-day cadence to render; null hides the mini-cadence bars entirely. */
const hasCadence = computed<boolean>(
  () => Array.isArray(props.cadence) && props.cadence.length > 0,
)

/**
 * Formatted apply-rate percentage, or null when there is no decision history.
 * Never falls back to a fabricated constant — an absent value renders the
 * "No decisions yet" empty state instead of an invented percentage.
 */
const applyRatePct = computed<string | null>(() =>
  typeof props.applyRate === 'number' ? asPct(props.applyRate) : null,
)

function setFilter(next: QueueFilter) {
  filter.value = next
  emit('filter-change', next)
}

/**
 * Chips to render. On a single-member workspace "Mine" is the whole queue and
 * "All" is its meaningless counterpart, so the pair is dropped and only Stale
 * remains. Stale is preserved in every case.
 */
const visibleFilters = computed<QueueFilter[]>(() =>
  props.authorPartitionAvailable ? ['all', 'mine', 'stale'] : ['stale'],
)

watch(
  () => props.authorPartitionAvailable,
  (available) => {
    // The partition can vanish under a live selection: the membership answer
    // arrives after first paint, or the last collaborator is removed. "Mine"
    // then has no chip to return to, so fall back to the unfiltered queue and
    // tell the parent, which re-resolves the active proposal.
    if (!available && filter.value === 'mine') {
      setFilter('all')
    }
  },
)

function onFilterPillClick(key: QueueFilter) {
  // With the pair hidden, Stale is the only chip, so it toggles against the
  // whole queue. Without this it would be a one-way switch into a filter the
  // reviewer has no visible control to leave.
  if (!props.authorPartitionAvailable && key === 'stale' && filter.value === 'stale') {
    setFilter('all')
    return
  }
  setFilter(key)
}
</script>

<template>
  <aside class="paper-review-rail" data-testid="paper-review-queue-rail">
    <div class="paper-review-rail__head">
      <div class="tk-eyebrow">
        {{
          $t(scopeLabel ? 'review.queueRail.eyebrowScoped' : 'review.queueRail.eyebrow', {
            awaiting: awaitingCount,
            stale: staleCount,
          })
        }}
      </div>
      <PaperScopeDisclosure
        v-if="scopeLabel && scopeClearLabel"
        :label="scopeLabel"
        :clear-label="scopeClearLabel"
        @clear="emit('clear-scope')"
      />
      <div
        class="paper-review-rail__filters"
        role="group"
        :aria-label="$t('review.queueRail.filters.label')"
      >
        <button
          v-for="key in visibleFilters"
          :key="key"
          type="button"
          class="paper-review-rail__pill"
          :class="{ 'paper-review-rail__pill--active': filter === key }"
          :aria-pressed="filter === key"
          @click="onFilterPillClick(key)"
        >{{ $t(`review.queueRail.filter.${key}`) }}</button>
      </div>
      <p class="paper-review-rail__risk-note tk-meta" role="note" data-testid="paper-review-risk-order-note">
        {{ $t('review.queueRail.riskNote') }}
      </p>
      <button
        v-if="dismissableCount >= 1"
        type="button"
        class="paper-review-rail__file-away"
        data-testid="queue-file-away-all"
        :disabled="busy"
        :aria-label="$t('review.queueRail.fileAway.label', { count: dismissableCount })"
        @click="emit('file-away-all')"
      >{{ $t('review.queueRail.fileAway.cta', { count: dismissableCount }) }}</button>
    </div>

    <div v-if="visible.length === 0" class="paper-review-rail__empty tk-meta">
      {{ $t('review.queueRail.empty') }}
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
      <div class="tk-eyebrow paper-review-rail__cadence-heading">
        {{ $t('review.queueRail.cadence.heading') }}
      </div>
      <ReviewMiniCadence v-if="hasCadence" :days="cadence" />
      <div
        v-if="applyRatePct !== null"
        class="tk-meta paper-review-rail__cadence-meta"
        data-testid="paper-review-apply-rate"
      >
        {{ $t('review.queueRail.cadence.applyRateLabel') }} <b>{{ applyRatePct }}</b>
      </div>
      <div
        v-else
        class="tk-meta paper-review-rail__cadence-meta paper-review-rail__cadence-empty"
        data-testid="paper-review-apply-rate-empty"
      >
        {{ $t('review.queueRail.cadence.applyRateEmpty') }}
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
.paper-review-rail__risk-note {
  margin: 10px 0 0;
  line-height: 1.45;
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
