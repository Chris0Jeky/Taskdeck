<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useNotificationStore } from '../store/notificationStore'
import { TdSkeleton } from '../components/ui'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import { getErrorDisplay } from '../composables/useErrorMapper'
import {
  groupNotifications,
  typeBorderClass,
  typeBadgeClass,
  typeLabel,
  type NotificationGroup,
  type TimeGroup,
} from '../composables/useNotificationGrouping'
import { useVirtualList } from '../composables/useVirtualList'
import type { NotificationItem } from '../types/notifications'
import { normalizeBoardIdQueryParam } from '../utils/navigation'

const notifications = useNotificationStore()
const route = useRoute()
const router = useRouter()
const unreadOnly = ref(false)
const inlineError = ref<string | null>(null)
const expandedGroups = ref<Set<string>>(new Set())

const items = computed(() => notifications.notifications)
const unreadCount = computed(() => items.value.filter((item) => !item.isRead).length)
const activeBoardId = computed(() => normalizeBoardIdQueryParam(route.query.boardId))

const grouped = computed<NotificationGroup[]>(() => groupNotifications(items.value))

/**
 * Flatten the grouped notification structure into a single list of display rows.
 * Each row is either a time header, a collapsed group summary, a collapse button,
 * or an individual notification item.
 */
type FlatRow =
  | { kind: 'time-header'; header: TimeGroup; key: string }
  | { kind: 'collapsed-summary'; group: NotificationGroup; key: string }
  | { kind: 'collapse-button'; group: NotificationGroup; key: string }
  | { kind: 'notification'; item: NotificationItem; group: NotificationGroup; key: string }

const flatRows = computed<FlatRow[]>(() => {
  const rows: FlatRow[] = []
  let lastHeader: TimeGroup | null = null

  for (const group of grouped.value) {
    // Insert a time header row when the time section changes
    if (group.timeHeader !== lastHeader) {
      rows.push({ kind: 'time-header', header: group.timeHeader, key: `header-${group.timeHeader}` })
      lastHeader = group.timeHeader
    }

    if (group.isCollapsed && !expandedGroups.value.has(group.key)) {
      // Collapsed group: show a single summary row
      rows.push({ kind: 'collapsed-summary', group, key: `collapsed-${group.key}` })
    } else {
      // Expanded group or single-item group
      if (group.isCollapsed) {
        // Add a collapse button row before the expanded items
        rows.push({ kind: 'collapse-button', group, key: `collapse-btn-${group.key}` })
      }
      for (const item of group.items) {
        rows.push({ kind: 'notification', item, group, key: `item-${item.id}` })
      }
    }
  }

  return rows
})

const _vl = useVirtualList({
  count: computed(() => flatRows.value.length),
  estimateSize: 100,
  overscan: 5,
})

// vue-tsc >=3.2.6 does not count ref="name" in templates as a script read;
// these refs are intentionally bound via template ref attributes.
// @ts-expect-error TS6133
const notifParentRef = _vl.parentRef
// @ts-expect-error TS6133
const notifVirtualItemEls = _vl.virtualItemEls
const notifVirtualRows = _vl.virtualRows
const notifTotalSize = _vl.totalSize
const notifTranslateY = _vl.translateY

const activeNotifIndex = ref(0)

function handleNotifKeydown(event: KeyboardEvent) {
  if (flatRows.value.length === 0) return
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    const next = Math.min(activeNotifIndex.value + 1, flatRows.value.length - 1)
    activeNotifIndex.value = next
    _vl.scrollToIndex(next)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    const prev = Math.max(activeNotifIndex.value - 1, 0)
    activeNotifIndex.value = prev
    _vl.scrollToIndex(prev)
  }
}

/** Accessor helpers to simplify template type narrowing for flat rows. */
function rowHeader(index: number): TimeGroup {
  const row = flatRows.value[index]
  if (row && row.kind === 'time-header') return row.header
  return '' as TimeGroup
}

function rowGroup(index: number): NotificationGroup {
  const row = flatRows.value[index]
  if (row && (row.kind === 'collapsed-summary' || row.kind === 'collapse-button')) return row.group
  return { key: '', timeHeader: '' as TimeGroup, isCollapsed: false, summaryLabel: '', items: [] } as unknown as NotificationGroup
}

function rowItem(index: number): NotificationItem {
  const row = flatRows.value[index]
  if (row && row.kind === 'notification') return row.item
  return {} as NotificationItem
}

function formatCadence(value: number | string): string {
  const normalized = String(value)
  if (normalized === '0' || normalized === 'Immediate') return 'Immediate'
  if (normalized === '1' || normalized === 'Digest') return 'Digest'
  return normalized
}

function normalizeSourceEntityType(value: string | null): string {
  return value?.trim().toLowerCase() ?? ''
}

function toggleGroupExpand(key: string) {
  if (expandedGroups.value.has(key)) {
    expandedGroups.value.delete(key)
  } else {
    expandedGroups.value.add(key)
  }
}

async function loadNotifications() {
  inlineError.value = null
  try {
    await notifications.fetchNotifications({
      unreadOnly: unreadOnly.value,
      ...(activeBoardId.value ? { boardId: activeBoardId.value } : {}),
      limit: 200,
    })
  } catch (e: unknown) {
    inlineError.value = getErrorDisplay(e, notifications.error || 'Failed to load notifications').message
  }
}

async function markAsRead(notificationId: string) {
  inlineError.value = null
  try {
    await notifications.markAsRead(notificationId)
  } catch (e: unknown) {
    inlineError.value = getErrorDisplay(e, notifications.error || 'Failed to mark notification as read').message
  }
}

async function markAllRead() {
  inlineError.value = null
  try {
    await notifications.markAllRead(activeBoardId.value || undefined)
  } catch (e: unknown) {
    inlineError.value = getErrorDisplay(e, notifications.error || 'Failed to mark all as read').message
  }
}

function destinationLabel(item: NotificationItem): string | null {
  if (normalizeSourceEntityType(item.sourceEntityType) === 'proposal' && item.sourceEntityId) {
    return 'Open Proposal'
  }

  if (item.boardId ?? activeBoardId.value) {
    return 'Open Board'
  }

  return null
}

function openNotificationDestination(item: NotificationItem) {
  if (normalizeSourceEntityType(item.sourceEntityType) === 'proposal' && item.sourceEntityId) {
    void router.push({
      name: 'workspace-review',
      query: item.boardId
        ? { boardId: item.boardId }
        : (activeBoardId.value ? { boardId: activeBoardId.value } : undefined),
      hash: `#proposal-${encodeURIComponent(item.sourceEntityId)}`,
    })
    return
  }

  const destinationBoardId = item.boardId || activeBoardId.value
  if (destinationBoardId) {
    void router.push(`/workspace/boards/${destinationBoardId}`)
  }
}

onMounted(() => {
  void loadNotifications()
})

watch([unreadOnly, activeBoardId], () => {
  void loadNotifications()
})
</script>

<template>
  <div class="paper-notifications max-w-[920px]">
    <header class="paper-notifications__header">
      <div class="paper-notifications__header-copy">
        <span class="tk-eyebrow paper-notifications__eyebrow">Workspace</span>
        <h1 class="tk-h2 paper-notifications__title">Notifications</h1>
        <p class="tk-lede paper-notifications__subtitle">{{ unreadCount }} unread</p>
        <p v-if="activeBoardId" class="paper-notifications__board-scope">
          Showing notifications linked to board {{ activeBoardId }}.
        </p>
      </div>
      <div class="paper-notifications__header-actions">
        <PaperHLBtn
          v-if="unreadCount > 0"
          class="paper-notifications__mark-all"
          @click="markAllRead"
        >
          Mark all read
        </PaperHLBtn>
        <PaperHLBtn class="paper-notifications__refresh" @click="loadNotifications">
          Refresh
        </PaperHLBtn>
      </div>
    </header>

    <div class="paper-notifications__filter">
      <label class="paper-notifications__checkbox">
        <input v-model="unreadOnly" type="checkbox" />
        <span>Show unread only</span>
      </label>
    </div>

    <div v-if="inlineError" class="paper-notifications__alert" role="alert">
      {{ inlineError }}
    </div>

    <div v-if="notifications.loading" class="paper-notifications__skeleton" role="status" aria-live="polite">
      <span class="sr-only">Loading notifications...</span>
      <div v-for="n in 4" :key="n" class="paper-notifications__skeleton-row">
        <div class="flex flex-col gap-2 flex-1">
          <div class="flex items-center gap-2">
            <TdSkeleton width="60px" height="20px" />
            <TdSkeleton width="200px" height="14px" />
          </div>
          <TdSkeleton width="80%" height="12px" />
          <div class="flex gap-3">
            <TdSkeleton width="70px" height="10px" />
            <TdSkeleton width="100px" height="10px" />
          </div>
        </div>
        <TdSkeleton width="80px" height="32px" />
      </div>
    </div>
    <div v-else-if="items.length === 0" class="paper-notifications__empty">No notifications found.</div>

    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- virtual scrollable list with keyboard handler -->
    <div
      v-else
      ref="notifParentRef"
      class="paper-notifications__virtual"
      tabindex="0"
      @keydown="handleNotifKeydown"
    >
      <div
        role="presentation"
        class="td-virtual-scroll-sizer"
        :style="{ '--td-virtual-size': `${notifTotalSize}px` }"
      >
        <div
          role="presentation"
          class="td-virtual-scroll-offset"
          :style="{ '--td-virtual-offset': `${notifTranslateY}px` }"
        >
          <div
            v-for="virtualRow in notifVirtualRows"
            :key="String(virtualRow.key)"
            :data-index="virtualRow.index"
            ref="notifVirtualItemEls"
            role="presentation"
          >
            <template v-if="flatRows[virtualRow.index]">
              <!-- Time header row -->
              <h2
                v-if="flatRows[virtualRow.index]!.kind === 'time-header'"
                class="tk-eyebrow paper-notifications__time-header"
              >
                {{ rowHeader(virtualRow.index) }}
              </h2>

              <!-- Collapsed group summary -->
              <button
                v-else-if="flatRows[virtualRow.index]!.kind === 'collapsed-summary'"
                class="paper-notifications__group-summary"
                :class="typeBorderClass(rowGroup(virtualRow.index).items[0].type)"
                @click="toggleGroupExpand(rowGroup(virtualRow.index).key)"
              >
                <div class="flex items-center gap-3">
                  <span
                    class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium"
                    :class="typeBadgeClass(rowGroup(virtualRow.index).items[0].type)"
                  >
                    {{ typeLabel(rowGroup(virtualRow.index).items[0].type) }}
                  </span>
                  <span class="paper-notifications__group-label">
                    {{ rowGroup(virtualRow.index).summaryLabel }}
                  </span>
                  <span class="paper-notifications__group-hint">
                    Click to expand
                  </span>
                </div>
              </button>

              <!-- Collapse button for expanded groups -->
              <div
                v-else-if="flatRows[virtualRow.index]!.kind === 'collapse-button'"
                class="flex items-center gap-2 mb-1"
              >
                <button
                  class="paper-notifications__collapse-btn"
                  @click="toggleGroupExpand(rowGroup(virtualRow.index).key)"
                >
                  Collapse {{ rowGroup(virtualRow.index).items.length }} {{ typeLabel(rowGroup(virtualRow.index).items[0].type).toLowerCase() }} notifications
                </button>
              </div>

              <!-- Individual notification item -->
              <div
                v-else-if="flatRows[virtualRow.index]!.kind === 'notification'"
                class="paper-notifications__row"
                :class="[
                  typeBorderClass(rowItem(virtualRow.index).type),
                  { 'paper-notifications__row--unread': !rowItem(virtualRow.index).isRead },
                ]"
              >
                <div class="flex flex-col gap-2">
                  <div class="flex items-center gap-2">
                    <span
                      class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium"
                      :class="typeBadgeClass(rowItem(virtualRow.index).type)"
                    >
                      {{ typeLabel(rowItem(virtualRow.index).type) }}
                    </span>
                    <span class="paper-notifications__row-title">
                      {{ rowItem(virtualRow.index).title }}
                    </span>
                  </div>
                  <div class="paper-notifications__row-message">{{ rowItem(virtualRow.index).message }}</div>
                  <div class="paper-notifications__row-meta">
                    <span v-if="rowItem(virtualRow.index).boardId">Board-linked</span>
                    <span>{{ formatCadence(rowItem(virtualRow.index).cadence) }}</span>
                    <span>{{ new Date(rowItem(virtualRow.index).createdAt).toLocaleString() }}</span>
                  </div>
                </div>
                <div class="flex flex-wrap justify-end gap-2">
                  <PaperHLBtn
                    v-if="destinationLabel(rowItem(virtualRow.index))"
                    class="paper-notifications__open"
                    @click="openNotificationDestination(rowItem(virtualRow.index))"
                  >
                    {{ destinationLabel(rowItem(virtualRow.index)) }}
                  </PaperHLBtn>
                  <PaperHLBtn
                    v-if="!rowItem(virtualRow.index).isRead"
                    variant="ember"
                    class="paper-notifications__mark-read"
                    @click="markAsRead(rowItem(virtualRow.index).id)"
                  >
                    Mark read
                  </PaperHLBtn>
                </div>
              </div>
            </template>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — NotificationInboxView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell.  The per-type accent
   classes still come from `composables/useNotificationGrouping.ts` (shared
   with the notification bell) and are intentionally untouched here. */

.paper-notifications {
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.paper-notifications__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--s-4, 16px);
  margin-bottom: var(--s-4, 16px);
}

.paper-notifications__header-copy { display: flex; flex-direction: column; gap: var(--s-2, 8px); }
.paper-notifications__eyebrow { color: var(--ember, #a8421f); }
.paper-notifications__title { margin: 0; font-size: var(--t-h2, 32px); }
.paper-notifications__subtitle { margin: 0; color: var(--ink-2, #3a352d); }

.paper-notifications__board-scope {
  margin: 0;
  font-size: var(--t-md, 13.5px);
  font-weight: 600;
  color: var(--ember, #a8421f);
}

.paper-notifications__header-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2, 8px);
  flex-shrink: 0;
}

.paper-notifications__filter { margin-bottom: var(--s-4, 16px); }

.paper-notifications__checkbox {
  display: inline-flex;
  align-items: center;
  gap: var(--s-2, 8px);
  cursor: pointer;
  color: var(--ink-2, #3a352d);
  font-size: var(--t-md, 13.5px);
}

.paper-notifications__alert {
  margin-bottom: var(--s-4, 16px);
  padding: var(--s-3, 12px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--overdue, #8c4a26);
  background: var(--overdue-tint, #ecd9c4);
  color: var(--overdue, #8c4a26);
}

.paper-notifications__skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  padding: var(--s-2, 8px) 0;
}

.paper-notifications__skeleton-row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--s-4, 16px);
  padding: var(--s-4, 16px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
}

.paper-notifications__empty {
  color: var(--mute, #6c6557);
  padding: var(--s-6, 24px) 0;
}

.paper-notifications__virtual {
  max-height: 70vh;
  overflow-y: auto;
  contain: layout paint;
  outline: none;
}

.paper-notifications__virtual:focus-visible {
  box-shadow: inset 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-notifications__time-header {
  margin: var(--s-4, 16px) 0 var(--s-3, 12px);
  color: var(--mute, #6c6557);
}

.paper-notifications__group-summary {
  width: 100%;
  text-align: left;
  padding: var(--s-4, 16px);
  margin-bottom: var(--s-3, 12px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  cursor: pointer;
  font-family: inherit;
  color: inherit;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-notifications__group-summary:hover { background: var(--paper-2, #ebe5d8); }

.paper-notifications__group-label { font-weight: 600; color: var(--ink-deep, #0a0908); }

.paper-notifications__group-hint {
  margin-left: auto;
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
}

.paper-notifications__collapse-btn {
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;
  font-family: inherit;
  font-size: var(--t-xs, 10.5px);
  color: var(--ember, #a8421f);
}

.paper-notifications__collapse-btn:hover { text-decoration: underline; }

.paper-notifications__row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--s-4, 16px);
  padding: var(--s-4, 16px);
  margin-bottom: var(--s-3, 12px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-notifications__row--unread { border-color: var(--ember, #a8421f); }

.paper-notifications__row-title { font-weight: 600; color: var(--ink-deep, #0a0908); }
.paper-notifications__row-message { color: var(--ink-2, #3a352d); font-size: var(--t-md, 13.5px); }

.paper-notifications__row-meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-3, 12px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
}
</style>
