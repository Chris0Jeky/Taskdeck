<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useNotificationStore } from '../store/notificationStore'
import { TdSkeleton } from '../components/ui'
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
  <div class="td-notifications max-w-[920px]">
    <header class="flex items-start justify-between gap-4 mb-4">
      <div>
        <h1 class="td-page-title">Notifications</h1>
        <p class="mt-1 text-[color:var(--td-text-secondary)]">
          {{ unreadCount }} unread
        </p>
        <p v-if="activeBoardId" class="mt-2 text-sm font-semibold text-[color:var(--td-color-primary)]">
          Showing notifications linked to board {{ activeBoardId }}.
        </p>
      </div>
      <div class="flex gap-2">
        <button
          v-if="unreadCount > 0"
          class="td-btn td-btn--secondary"
          @click="markAllRead"
        >
          Mark all read
        </button>
        <button class="td-btn td-btn--secondary" @click="loadNotifications">
          Refresh
        </button>
      </div>
    </header>

    <div class="mb-4">
      <label class="inline-flex items-center gap-2 text-[color:var(--td-text-secondary)]">
        <input v-model="unreadOnly" type="checkbox" />
        <span>Show unread only</span>
      </label>
    </div>

    <div v-if="inlineError" class="td-alert td-alert--error" role="alert">
      {{ inlineError }}
    </div>

    <div v-if="notifications.loading" class="td-notification-skeleton" role="status" aria-live="polite">
      <span class="sr-only">Loading notifications...</span>
      <div v-for="n in 4" :key="n" class="td-notification-skeleton__row">
        <div style="display: flex; flex-direction: column; gap: 0.5rem; flex: 1">
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <TdSkeleton width="60px" height="20px" />
            <TdSkeleton width="200px" height="14px" />
          </div>
          <TdSkeleton width="80%" height="12px" />
          <div style="display: flex; gap: 0.75rem">
            <TdSkeleton width="70px" height="10px" />
            <TdSkeleton width="100px" height="10px" />
          </div>
        </div>
        <TdSkeleton width="80px" height="32px" />
      </div>
    </div>
    <div v-else-if="items.length === 0" class="td-notification-empty">No notifications found.</div>

    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- virtual scrollable list with keyboard handler -->
    <div
      v-else
      ref="notifParentRef"
      class="td-notif-virtual"
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
                class="text-sm font-semibold uppercase tracking-wide text-[color:var(--td-text-tertiary)] mb-3 mt-4"
              >
                {{ rowHeader(virtualRow.index) }}
              </h2>

              <!-- Collapsed group summary -->
              <button
                v-else-if="flatRows[virtualRow.index]!.kind === 'collapsed-summary'"
                class="w-full text-left rounded-lg border border-[color:var(--td-border-default)] bg-[color:var(--td-surface-primary)] p-4 hover:bg-[color:var(--td-surface-secondary)] transition-colors mb-3"
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
                  <span class="font-medium text-[color:var(--td-text-primary)]">
                    {{ rowGroup(virtualRow.index).summaryLabel }}
                  </span>
                  <span class="text-xs text-[color:var(--td-text-tertiary)] ml-auto">
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
                  class="text-xs text-[color:var(--td-color-primary)] hover:underline"
                  @click="toggleGroupExpand(rowGroup(virtualRow.index).key)"
                >
                  Collapse {{ rowGroup(virtualRow.index).items.length }} {{ typeLabel(rowGroup(virtualRow.index).items[0].type).toLowerCase() }} notifications
                </button>
              </div>

              <!-- Individual notification item -->
              <div
                v-else-if="flatRows[virtualRow.index]!.kind === 'notification'"
                class="td-notification-row flex justify-between gap-4 items-start rounded-lg border border-[color:var(--td-border-default)] bg-[color:var(--td-surface-primary)] p-4 mb-3"
                :class="[
                  typeBorderClass(rowItem(virtualRow.index).type),
                  { 'border-[color:var(--td-color-primary)]': !rowItem(virtualRow.index).isRead },
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
                    <span class="font-semibold text-[color:var(--td-text-primary)]">
                      {{ rowItem(virtualRow.index).title }}
                    </span>
                  </div>
                  <div class="text-[color:var(--td-text-secondary)]">{{ rowItem(virtualRow.index).message }}</div>
                  <div class="flex flex-wrap gap-3 text-sm text-[color:var(--td-text-tertiary)]">
                    <span v-if="rowItem(virtualRow.index).boardId">Board-linked</span>
                    <span>{{ formatCadence(rowItem(virtualRow.index).cadence) }}</span>
                    <span>{{ new Date(rowItem(virtualRow.index).createdAt).toLocaleString() }}</span>
                  </div>
                </div>
                <div class="flex flex-wrap justify-end gap-2">
                  <button
                    v-if="destinationLabel(rowItem(virtualRow.index))"
                    class="td-btn td-btn--secondary"
                    @click="openNotificationDestination(rowItem(virtualRow.index))"
                  >
                    {{ destinationLabel(rowItem(virtualRow.index)) }}
                  </button>
                  <button
                    v-if="!rowItem(virtualRow.index).isRead"
                    class="td-btn td-btn--primary"
                    @click="markAsRead(rowItem(virtualRow.index).id)"
                  >
                    Mark read
                  </button>
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
.td-notification-skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  padding: var(--td-space-2) 0;
}

.td-notification-empty {
  color: var(--td-text-secondary);
  padding: var(--td-space-6) 0;
}

.td-notification-skeleton__row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-4);
  padding: var(--td-space-4);
  border-radius: var(--td-radius-lg);
  border: 1px solid var(--td-border-default);
  background: var(--td-surface-primary);
}

.td-alert {
  margin-bottom: var(--td-space-4);
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
}

.td-alert--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: none;
  cursor: pointer;
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border: 1px solid var(--td-border-default);
}

.td-notif-virtual {
  max-height: 70vh;
  overflow-y: auto;
  contain: layout paint;
  outline: none;
}

.td-notif-virtual:focus-visible {
  box-shadow: inset 0 0 0 2px rgba(255, 77, 77, 0.35);
}
</style>
