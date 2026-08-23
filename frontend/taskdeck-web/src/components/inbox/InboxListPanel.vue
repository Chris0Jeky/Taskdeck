<script setup lang="ts">
import { computed } from 'vue'
import { TdBadge, TdEmptyState, TdInlineAlert, TdSkeleton } from '../ui'
import { statusLabel, statusBadgeVariant, sourceLabel } from './inboxUtils'
import { useVirtualList } from '../../composables/useVirtualList'
import type { CaptureItemSummary } from '../../types/capture'

const props = defineProps<{
  items: CaptureItemSummary[]
  loadingList: boolean
  listError: string | null
  hasItems: boolean
  batchBusy: boolean
  activeItemIndex: number
  selectedItemId: string | null
  selectedIds: Set<string>
  activeDescendantId: string | undefined
  readOnly?: boolean
}>()

const emit = defineEmits<{
  (e: 'open-item', item: CaptureItemSummary, index: number): void
  (e: 'set-active-index', index: number): void
  (e: 'keydown', event: KeyboardEvent): void
  (e: 'toggle-item-selection', itemId: string): void
  (e: 'toggle-select-all'): void
  (e: 'clear-selection'): void
  (e: 'batch-action', action: 'triage' | 'ignore' | 'cancel'): void
  (e: 'open-capture-modal'): void
  (e: 'open-route', path: string): void
  (e: 'open-review'): void
  (e: 'load-inbox'): void
}>()

const hasSelection = computed(() => props.selectedIds.size > 0)
const selectionCount = computed(() => props.selectedIds.size)
const allSelected = computed(() =>
  props.items.length > 0 && props.selectedIds.size === props.items.length
)

const listRole = computed(() =>
  props.hasItems && !props.loadingList && !props.listError ? 'listbox' : 'group'
)

const _vl = useVirtualList({
  count: computed(() => props.items.length),
  estimateSize: 80,
  overscan: 5,
})
// vue-tsc >=3.2.6 does not count ref="name" in templates as a script read;
// these refs are intentionally bound via template ref attributes.
// @ts-expect-error TS6133
const parentRef = _vl.parentRef
// @ts-expect-error TS6133
const virtualItemEls = _vl.virtualItemEls
const virtualRows = _vl.virtualRows
const virtualTotalSize = _vl.totalSize
const virtualTranslateY = _vl.translateY

defineExpose({
  scrollToIndex: _vl.scrollToIndex,
})
</script>

<template>
  <section class="td-inbox__list-panel">
    <div class="td-inbox__list-header">
      <div class="td-inbox__list-header-left">
        <label v-if="items.length > 0 && !readOnly" class="td-inbox__select-all" data-testid="select-all">
          <input
            type="checkbox"
            :checked="allSelected"
            :indeterminate="hasSelection && !allSelected"
            @change="emit('toggle-select-all')"
          />
          <span v-if="hasSelection">{{ selectionCount }} selected</span>
          <span v-else>Select all</span>
        </label>
        <h2 v-if="!hasSelection">Items</h2>
      </div>
      <span class="td-inbox__count">{{ items.length }}</span>
    </div>

    <div v-if="hasSelection && !readOnly" class="td-inbox__batch-bar" data-testid="batch-action-bar">
      <button
        class="td-btn td-btn--primary td-btn--sm"
        :disabled="batchBusy"
        @click="emit('batch-action', 'triage')"
      >
        {{ batchBusy ? 'Processing...' : `Triage (${selectionCount})` }}
      </button>
      <button
        class="td-btn td-btn--danger td-btn--sm"
        :disabled="batchBusy"
        @click="emit('batch-action', 'ignore')"
      >
        {{ batchBusy ? 'Processing...' : `Ignore (${selectionCount})` }}
      </button>
      <button
        class="td-btn td-btn--secondary td-btn--sm"
        :disabled="batchBusy"
        @click="emit('batch-action', 'cancel')"
      >
        {{ batchBusy ? 'Processing...' : `Cancel (${selectionCount})` }}
      </button>
      <button
        class="td-btn td-btn--ghost td-btn--sm"
        @click="emit('clear-selection')"
      >
        Clear
      </button>
    </div>

    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- dynamic role (listbox/group) + tabindex="0" makes this interactive -->
    <div
      ref="parentRef"
      class="td-inbox__list"
      tabindex="0"
      :role="listRole"
      aria-label="Inbox items"
      :aria-activedescendant="activeDescendantId"
      @keydown="emit('keydown', $event)"
    >
      <div v-if="loadingList" class="td-inbox__skeleton-list" data-testid="inbox-loading-skeleton" role="status">
        <span class="sr-only">Loading inbox items...</span>
        <div v-for="n in 5" :key="n" class="td-inbox__skeleton-row">
          <div class="td-inbox__skeleton-head">
            <TdSkeleton width="60px" height="18px" />
            <TdSkeleton width="48px" height="18px" />
          </div>
          <TdSkeleton width="85%" height="14px" />
          <TdSkeleton width="55%" height="14px" />
          <TdSkeleton width="100px" height="12px" />
        </div>
      </div>
      <div v-else-if="listError" class="td-inbox__list-error" data-testid="inbox-list-error">
        <TdInlineAlert variant="error">
          <p>{{ listError }}</p>
        </TdInlineAlert>
        <button
          class="td-btn td-btn--secondary td-btn--sm td-inbox__retry-btn"
          data-testid="inbox-retry-btn"
          @click="emit('load-inbox')"
        >
          Retry
        </button>
      </div>
      <TdEmptyState
        v-else-if="!hasItems"
        :title="readOnly ? 'No retained captures found' : 'No capture items yet'"
        :description="readOnly
          ? 'This archived board has no retained capture records in the current scope.'
          : 'Capture a note or transcript to get started. Once triage runs, proposals will appear in Review.'"
        data-testid="inbox-empty-state"
      >
        <template #icon>
          <svg width="40" height="40" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path d="M3 8l9-5 9 5v8l-9 5-9-5V8z" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/>
            <path d="M3 8l9 5m0 0l9-5m-9 5v9" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/>
          </svg>
        </template>
        <template #action>
          <div class="td-inbox__empty-actions">
            <button
              v-if="!readOnly"
              class="td-btn td-btn--primary td-btn--sm"
              aria-label="Open capture modal to add a new inbox item"
              @click="emit('open-capture-modal')"
            >
              + New Capture
            </button>
            <button class="td-btn td-btn--secondary td-btn--sm" @click="emit('open-route', '/workspace/home')">Open Home</button>
            <button class="td-btn td-btn--secondary td-btn--sm" @click="emit('open-route', '/workspace/today')">Open Today</button>
            <button class="td-btn td-btn--secondary td-btn--sm" @click="emit('open-review')">Open Review</button>
          </div>
        </template>
      </TdEmptyState>

      <div
        v-if="hasItems && !loadingList && !listError"
        role="presentation"
        class="td-virtual-scroll-sizer"
        :style="{ '--td-virtual-size': `${virtualTotalSize}px` }"
      >
        <div
          role="presentation"
          class="td-virtual-scroll-offset"
          :style="{ '--td-virtual-offset': `${virtualTranslateY}px` }"
        >
          <div
            v-for="virtualRow in virtualRows"
            :key="String(virtualRow.key)"
            :data-index="virtualRow.index"
            ref="virtualItemEls"
            role="presentation"
          >
            <template v-if="items[virtualRow.index]">
              <div
                :id="`td-inbox-option-${virtualRow.index}`"
                :data-inbox-index="virtualRow.index"
                data-testid="inbox-item"
                :data-capture-id="items[virtualRow.index]!.id"
                :class="[
                  'td-inbox-row',
                  virtualRow.index % 2 === 1 ? 'td-inbox-row--alt' : '',
                  virtualRow.index === activeItemIndex ? 'td-inbox-row--active' : '',
                  selectedItemId === items[virtualRow.index]!.id ? 'td-inbox-row--selected' : ''
                ]"
                role="option"
                tabindex="-1"
                :aria-selected="selectedItemId === items[virtualRow.index]!.id"
                @mouseenter="emit('set-active-index', virtualRow.index)"
                @focusin="emit('set-active-index', virtualRow.index)"
                @click="emit('open-item', items[virtualRow.index]!, virtualRow.index)"
                @keydown.enter="emit('open-item', items[virtualRow.index]!, virtualRow.index)"
              >
                <div class="td-inbox-row__head">
                  <input
                    v-if="!readOnly"
                    type="checkbox"
                    class="td-inbox-row__checkbox"
                    data-testid="inbox-item-checkbox"
                    :aria-label="`Select item ${virtualRow.index + 1}`"
                    :checked="selectedIds.has(items[virtualRow.index]!.id)"
                    @click.stop="emit('toggle-item-selection', items[virtualRow.index]!.id)"
                  />
                  <TdBadge :variant="statusBadgeVariant(items[virtualRow.index]!.status)" size="sm">{{ statusLabel(items[virtualRow.index]!.status) }}</TdBadge>
                  <TdBadge variant="default" size="sm">{{ sourceLabel(items[virtualRow.index]!.source) }}</TdBadge>
                </div>
                <p class="td-inbox-row__excerpt">{{ items[virtualRow.index]!.textExcerpt }}</p>
                <p class="td-inbox-row__meta">{{ new Date(items[virtualRow.index]!.createdAt).toLocaleString() }}</p>
              </div>
            </template>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.td-inbox__list-panel {
  display: flex;
  flex-direction: column;
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-lg);
  min-height: 580px;
  background: var(--td-surface-container, #201f1f);
}

/* ---- List header ---- */

.td-inbox__list-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--td-space-4);
  border-bottom: 0.5px solid var(--td-border-default);
}

.td-inbox__list-header-left {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
}

.td-inbox__select-all {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  cursor: pointer;
}

.td-inbox__select-all input[type="checkbox"] {
  cursor: pointer;
}

.td-inbox__batch-bar {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  padding: var(--td-space-2) var(--td-space-4);
  background: var(--td-surface-container-highest, #2a2a2a);
  border-bottom: 0.5px solid var(--td-border-default);
}

.td-inbox__list-header h2 {
  font-family: 'Manrope', system-ui, sans-serif;
  color: var(--td-text-primary);
}

.td-inbox__count {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.2em;
  color: var(--td-text-tertiary);
  background: var(--td-surface-container-highest);
  padding: 2px 10px;
  border-radius: var(--td-radius-sm);
}

/* ---- Scrollable list ---- */

.td-inbox__list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  padding: var(--td-space-3);
  overflow-y: auto;
  outline: none;
}

.td-inbox__list:focus-visible {
  box-shadow: inset 0 0 0 2px rgba(255, 77, 77, 0.35);
}

/* ---- List rows ---- */

.td-inbox-row {
  text-align: left;
  border: 0.5px solid var(--td-border-ghost);
  border-left: 2px solid transparent;
  border-radius: var(--td-radius-md);
  background: var(--td-surface-container, #201f1f);
  padding: var(--td-space-3);
  cursor: pointer;
  transition: background var(--td-transition-fast, 120ms) ease,
              border-color var(--td-transition-fast, 120ms) ease;
}

.td-inbox-row--alt {
  background: var(--td-surface-container-low, #1e1d1d);
}

.td-inbox-row:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-inbox-row--active {
  background: var(--td-surface-bright, #3a3939);
  border-left-color: var(--td-color-ember, #ff4d4d);
}

.td-inbox-row--selected {
  background: var(--td-surface-high, #2a2a2a);
  border-left-color: var(--td-color-ember, #ff4d4d);
  box-shadow: var(--td-shadow-sm, 0 1px 3px rgba(0, 0, 0, 0.4));
}

.td-inbox-row__head {
  display: flex;
  gap: var(--td-space-2);
  margin-bottom: var(--td-space-2);
}

.td-inbox-row__checkbox {
  cursor: pointer;
  flex-shrink: 0;
}

.td-inbox-row__excerpt {
  color: var(--td-text-primary);
  margin-bottom: var(--td-space-2);
  font-size: var(--td-font-sm);
  font-weight: 400;
  line-height: 1.5;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.td-inbox-row__meta {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-xs);
}

/* ---- Skeleton loading ---- */

.td-inbox__skeleton-list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  padding: var(--td-space-3);
}

.td-inbox__skeleton-row {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  padding: var(--td-space-3);
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-md);
}

.td-inbox__skeleton-head {
  display: flex;
  gap: var(--td-space-2);
}

/* ---- List error ---- */

.td-inbox__list-error {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--td-space-3);
  padding: var(--td-space-4);
}

.td-inbox__retry-btn {
  align-self: center;
}

/* ---- Empty state actions ---- */

.td-inbox__empty-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  justify-content: center;
}

/* ---- Responsive ---- */

@media (max-width: 1024px) {
  .td-inbox__list-panel {
    min-height: 320px;
  }
}

@media (max-width: 640px) {
  .td-inbox__list-panel {
    min-height: auto;
    border-radius: var(--td-radius-md);
    max-height: 50vh;
    max-height: 50dvh;
  }

  .td-inbox-row {
    padding: var(--td-space-3) var(--td-space-3);
    min-height: 44px;
  }

  .td-inbox-row__excerpt {
    font-size: var(--td-font-sm);
    line-height: 1.5;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }

  .td-inbox__empty-actions {
    flex-direction: column;
    width: 100%;
  }

  .td-inbox__empty-actions .td-btn {
    width: 100%;
    min-height: 44px;
    justify-content: center;
  }
}
</style>
