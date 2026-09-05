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
  /** Batch selection is deliberately narrower than ordinary single-review actionability. */
  batchEligible?: boolean
  batchSelected?: boolean
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
    /** Eligible proposals selected for the approve-only confirmation. */
    batchSelectedCount?: number
    /**
     * Already-approved proposals the reviewer can apply in one batch (#1307). Distinct from
     * `batchSelectedCount`: approve and execute stay two explicit steps (ADR-0003 / GP-06), so this
     * count is derived from what is already Approved, never from the approve selection.
     */
    batchExecutableCount?: number
    /** Disables the bulk file-away action while any review action is in flight. */
    busy?: boolean
    recentlyApplied: RecentlyAppliedRow[]
    /**
     * Real 7-day activity counts (oldest → newest). Omit when there is no
     * decision history — the mini-cadence bars are hidden rather than invented.
     */
    cadence?: number[]
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
    /**
     * Whether a queue read has landed for the board scope `awaitingCount`
     * belongs to (#2599 item 1). It replaced a `loading` flag, which was the
     * wrong question: an explicit reload raises that without clearing the
     * queue, so the rail unmounted this announcement for the length of every
     * reload and remounted it with the identical sentence — and a node addition
     * inside a live region is spoken. The reviewer heard the same count read
     * back after a Refresh, or after filing a settled proposal away, for a
     * queue that had not moved.
     *
     * False covers the cases where the count is not a count of the queue on
     * screen: before the first read lands, after a board-filter change until
     * the new scope's read lands, and for a scope no read has landed for,
     * including one whose only read failed. It stays TRUE after a later read
     * failed — the rows on screen are still the last landed answer, and
     * withholding there would put back the count -> '' -> count flicker for a
     * read that changed nothing. A failing refresh is reported by the
     * degraded/refused disclosures instead (#2214).
     *
     * Optional and defaulting to `false`, which WITHHOLDS: a parent that cannot
     * say a read has landed cannot have its count spoken, because 0 from a
     * never-read queue is the #2593 defect. `PaperReviewView` passes the shared
     * composable's `queueScopeLoaded`, the same signal `LegacyReviewView` gates
     * its own region on (#1124 / ADR-0038).
     */
    queueScopeLoaded?: boolean
    /**
     * Whether the queue was withdrawn rather than read (#2214 round 2). A
     * current-scope 403 sets `queueAccessRevoked` AND clears the queue, so
     * `awaitingCount` drops to 0 for a reason that is not "nothing is awaiting
     * review" — and because that is a CHANGE, an ungated live region speaks it.
     * Kept separate from `queueScopeLoaded` so the parent passes its two real
     * states rather than a derived boolean whose reason is lost at the call site;
     * `PaperReviewView` passes its own `queueAccessRevoked`.
     */
    queueUnavailable?: boolean
    /**
     * The identity of the queue `awaitingCount` counts, as one primitive
     * (#2214 item 4). It is the `key` of the node carrying the announcement,
     * never spoken text.
     *
     * A live region only speaks when it changes, so a poll that removed one
     * pending proposal and added another rendered a byte-identical
     * "3 proposals awaiting review." and announced nothing. Re-keying replaces
     * that node inside a region that stays mounted, which is the node addition
     * `aria-live`'s default `aria-relevant="additions text"` announces.
     *
     * The rail cannot derive this itself: `items` is the whole visible queue,
     * not the awaiting set the count is about, and a rail-local derivation is
     * exactly how the two skins drift (#1124 / ADR-0038). `PaperReviewView`
     * passes the shared composable's `queueAnnouncementKey`, the same value
     * `LegacyReviewView` keys its own region on. Optional and defaulting to a
     * constant, so a parent that does not pass it keeps today's behaviour.
     */
    announcementKey?: string
  }>(),
  {
    dismissableCount: 0,
    batchSelectedCount: 0,
    batchExecutableCount: 0,
    busy: false,
    authorPartitionAvailable: true,
    queueScopeLoaded: false,
    queueUnavailable: false,
    announcementKey: '',
  },
)

const emit = defineEmits<{
  (event: 'select', id: string): void
  (event: 'filter-change', filter: QueueFilter): void
  (event: 'file-away-all'): void
  (event: 'toggle-batch', id: string): void
  (event: 'request-batch-approval'): void
  (event: 'request-batch-execute'): void
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

/**
 * Whether `awaitingCount` is a real count right now (#2214, #2599 item 1). A
 * queue no read has landed for in this scope and one whose access was revoked
 * both carry 0 because nothing has been read, not because nothing awaits
 * review; neither is speakable. Kept in step with
 * `LegacyReviewView.countIsAnnounceable`, which gates the identical region on
 * the same two states (#1124 / ADR-0038).
 */
const countIsAnnounceable = computed<boolean>(
  () => props.queueScopeLoaded && !props.queueUnavailable,
)

/** The rail's own root, so the focus handoff below stays inside this subtree. */
const railRef = ref<HTMLElement | null>(null)

/**
 * Move focus to the first row of the queue, reporting whether there was one
 * (#2599 item 2).
 *
 * The target-unavailable panel's return control removes the element focus is
 * in, so focus would fall to `<body>`: nothing announced, and the next
 * keystroke acting on nothing. `PaperReviewView` calls this to hand focus to
 * the queue the panel was standing in front of, and falls back to its own empty
 * state when this reports `false`.
 *
 * The rail owns the rows, so it owns the handoff: a parent reaching into this
 * subtree for a row element is the seam the two skins drift at. Document order
 * is the queue's rendered order, which is why this reads the DOM rather than a
 * `v-for` ref array (Vue does not promise those match the source order).
 *
 * Legacy does NOT land on the same kind of element, and that divergence is
 * deliberate rather than drift (#2599 item 2). A row here is a `<button>`, so
 * focus announces that row and Enter selects it; Legacy's rows are not
 * focusable at all, so it focuses its queue `<section>`, which announces the
 * region label and takes Enter as nothing. The shared rule is the rule about
 * WHERE focus goes — the queue that replaced the panel, else the empty state
 * that did — not the element type, which each skin's queue widget decides.
 */
function focusFirstQueueRow(): boolean {
  const first = railRef.value?.querySelector<HTMLElement>('.paper-review-rail__queue-row button')
  if (!first) return false
  first.focus()
  return true
}

defineExpose({ focusFirstQueueRow })

/** Real 7-day cadence to render; null hides the mini-cadence bars entirely. */
const hasCadence = computed<boolean>(
  () => Array.isArray(props.cadence) && props.cadence.length > 0,
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
  <aside ref="railRef" class="paper-review-rail" data-testid="paper-review-queue-rail">
    <div class="paper-review-rail__head">
      <div class="tk-eyebrow">
        {{
          $t(scopeLabel ? 'review.queueRail.eyebrowScoped' : 'review.queueRail.eyebrow', {
            awaiting: awaitingCount,
            stale: staleCount,
          })
        }}
      </div>
      <!--
        The awaiting count now changes without any user action (#2194's bounded
        poll), so a screen-reader user gets no notification that the queue moved
        under them. The eyebrow itself cannot carry the live region: it also
        renders the stale count and is rewritten by filter clicks, which would
        make it chatter on ordinary interaction.

        The region stays MOUNTED while the count is unspeakable and only
        withholds its content (#2214): a live region inserted at the same moment
        its text appears is unreliably announced, so gating with `v-if` would
        trade one defect for another.

        Only the node INSIDE it is keyed and replaced, on `announcementKey`, so
        a queue whose contents changed without changing size is announced once
        with the sentence it always had (#2214 item 4).
      -->
      <p
        class="sr-only"
        role="status"
        aria-live="polite"
        data-testid="paper-review-queue-live"
      ><span
        v-if="countIsAnnounceable"
        :key="announcementKey"
        data-testid="paper-review-queue-announcement"
      >{{ $t('review.queueRail.liveAnnounce', { count: awaitingCount }, awaitingCount) }}</span></p>
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
        v-if="batchSelectedCount > 0"
        type="button"
        class="paper-review-rail__batch-approve"
        data-testid="queue-batch-approve"
        :disabled="busy"
        :aria-label="$t('review.batchApprove.requestLabel', { count: batchSelectedCount }, batchSelectedCount)"
        @click="emit('request-batch-approval')"
      >{{ $t('review.batchApprove.request', { count: batchSelectedCount }, batchSelectedCount) }}</button>
      <button
        v-if="batchExecutableCount > 0"
        type="button"
        class="paper-review-rail__batch-execute"
        data-testid="queue-batch-execute"
        :disabled="busy"
        :aria-label="$t('review.batchExecute.requestLabel', { count: batchExecutableCount }, batchExecutableCount)"
        @click="emit('request-batch-execute')"
      >{{ $t('review.batchExecute.request', { count: batchExecutableCount }, batchExecutableCount) }}</button>
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

    <div
      v-for="item in visible"
      :key="item.id"
      class="paper-review-rail__queue-row"
      :class="{ 'paper-review-rail__queue-row--selectable': item.batchEligible }"
    >
      <label
        v-if="item.batchEligible"
        class="paper-review-rail__batch-selector"
      >
        <input
          type="checkbox"
          :checked="item.batchSelected"
          :disabled="busy"
          :aria-label="$t('review.batchApprove.selectLabel', { title: item.title })"
          :data-testid="`queue-batch-select-${item.id}`"
          @change="emit('toggle-batch', item.id)"
        />
      </label>
      <ReviewQueueItem
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
    </div>

    <ReviewRecentApplied
      :rows="recentlyApplied"
      :active-id="activeId"
      @select="emit('select', $event)"
    />

    <div class="paper-review-rail__cadence">
      <div class="tk-eyebrow paper-review-rail__cadence-heading">
        {{ $t('review.queueRail.cadence.heading') }}
      </div>
      <ReviewMiniCadence v-if="hasCadence" :days="cadence" />
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
.paper-review-rail__batch-approve {
  margin-top: 10px;
  width: 100%;
  font-family: var(--mono);
  font-size: 10.5px;
  font-weight: 650;
  letter-spacing: 0.04em;
  padding: 7px 8px;
  background: var(--ink);
  border: 1px solid var(--ink);
  color: var(--paper);
  cursor: pointer;
  text-align: left;
}
.paper-review-rail__batch-approve:hover:not(:disabled) {
  opacity: 0.88;
}
.paper-review-rail__batch-approve:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.paper-review-rail__batch-execute {
  margin-top: 8px;
  width: 100%;
  font-family: var(--mono);
  font-size: 10.5px;
  font-weight: 650;
  letter-spacing: 0.04em;
  padding: 7px 8px;
  background: var(--paper-card);
  border: 1px solid var(--ink);
  color: var(--ink);
  cursor: pointer;
  text-align: left;
}
.paper-review-rail__batch-execute:hover:not(:disabled) {
  background: var(--ink);
  color: var(--paper);
}
.paper-review-rail__batch-execute:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.paper-review-rail__queue-row {
  position: relative;
}
.paper-review-rail__queue-row--selectable {
  display: grid;
  grid-template-columns: 34px minmax(0, 1fr);
  align-items: stretch;
}
.paper-review-rail__batch-selector {
  display: grid;
  place-items: start center;
  padding-top: 18px;
  border-bottom: 1px solid var(--line-soft);
  cursor: pointer;
}
.paper-review-rail__batch-selector input {
  width: 15px;
  height: 15px;
  accent-color: var(--ink);
}
.paper-review-rail__batch-selector:focus-within {
  outline: 2px solid var(--ink);
  outline-offset: -2px;
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
</style>
