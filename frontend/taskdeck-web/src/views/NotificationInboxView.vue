<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useNotificationStore } from '../store/notificationStore'
import { getErrorDisplay } from '../composables/useErrorMapper'
import {
  groupNotifications,
  typeBorderClass,
  typeBadgeClass,
  typeLabel,
  type NotificationGroup,
  type TimeGroup,
} from '../composables/useNotificationGrouping'
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

/** Distinct time headers in display order */
const timeHeaders = computed<TimeGroup[]>(() => {
  const seen = new Set<TimeGroup>()
  const result: TimeGroup[] = []
  for (const g of grouped.value) {
    if (!seen.has(g.timeHeader)) {
      seen.add(g.timeHeader)
      result.push(g.timeHeader)
    }
  }
  return result
})

function groupsForHeader(header: TimeGroup): NotificationGroup[] {
  return grouped.value.filter((g) => g.timeHeader === header)
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
    await notifications.markAllRead(activeBoardId.value ?? undefined)
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

    <div v-if="notifications.loading" class="td-placeholder">Loading notifications...</div>
    <div v-else-if="items.length === 0" class="td-placeholder">No notifications found.</div>

    <template v-else>
      <section v-for="header in timeHeaders" :key="header" class="mb-6">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-[color:var(--td-text-tertiary)] mb-3">
          {{ header }}
        </h2>

        <ul class="flex flex-col gap-3 list-none m-0 p-0">
          <template v-for="group in groupsForHeader(header)" :key="group.key">
            <!-- Collapsed group summary -->
            <li v-if="group.isCollapsed && !expandedGroups.has(group.key)">
              <button
                class="w-full text-left rounded-lg border border-[color:var(--td-border-default)] bg-[color:var(--td-surface-primary)] p-4 hover:bg-[color:var(--td-surface-secondary)] transition-colors"
                :class="typeBorderClass(group.items[0].type)"
                @click="toggleGroupExpand(group.key)"
              >
                <div class="flex items-center gap-3">
                  <span
                    class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium"
                    :class="typeBadgeClass(group.items[0].type)"
                  >
                    {{ typeLabel(group.items[0].type) }}
                  </span>
                  <span class="font-medium text-[color:var(--td-text-primary)]">
                    {{ group.summaryLabel }}
                  </span>
                  <span class="text-xs text-[color:var(--td-text-tertiary)] ml-auto">
                    Click to expand
                  </span>
                </div>
              </button>
            </li>

            <!-- Expanded group or single item -->
            <template v-else>
              <!-- Collapse button for expanded groups -->
              <li v-if="group.isCollapsed" class="flex items-center gap-2 mb-1">
                <button
                  class="text-xs text-[color:var(--td-color-primary)] hover:underline"
                  @click="toggleGroupExpand(group.key)"
                >
                  Collapse {{ group.items.length }} {{ typeLabel(group.items[0].type).toLowerCase() }} notifications
                </button>
              </li>

              <li
                v-for="item in group.items"
                :key="item.id"
                class="td-notification-row flex justify-between gap-4 items-start rounded-lg border border-[color:var(--td-border-default)] bg-[color:var(--td-surface-primary)] p-4"
                :class="[
                  typeBorderClass(item.type),
                  { 'border-[color:var(--td-color-primary)]': !item.isRead },
                ]"
              >
                <div class="flex flex-col gap-2">
                  <div class="flex items-center gap-2">
                    <span
                      class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium"
                      :class="typeBadgeClass(item.type)"
                    >
                      {{ typeLabel(item.type) }}
                    </span>
                    <span class="font-semibold text-[color:var(--td-text-primary)]">
                      {{ item.title }}
                    </span>
                  </div>
                  <div class="text-[color:var(--td-text-secondary)]">{{ item.message }}</div>
                  <div class="flex flex-wrap gap-3 text-sm text-[color:var(--td-text-tertiary)]">
                    <span v-if="item.boardId">Board-linked</span>
                    <span>{{ formatCadence(item.cadence) }}</span>
                    <span>{{ new Date(item.createdAt).toLocaleString() }}</span>
                  </div>
                </div>
                <div class="flex flex-wrap justify-end gap-2">
                  <button
                    v-if="destinationLabel(item)"
                    class="td-btn td-btn--secondary"
                    @click="openNotificationDestination(item)"
                  >
                    {{ destinationLabel(item) }}
                  </button>
                  <button
                    v-if="!item.isRead"
                    class="td-btn td-btn--primary"
                    @click="markAsRead(item.id)"
                  >
                    Mark read
                  </button>
                </div>
              </li>
            </template>
          </template>
        </ul>
      </section>
    </template>
  </div>
</template>

<style scoped>
.td-placeholder {
  color: var(--td-text-secondary);
  padding: var(--td-space-6) 0;
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
</style>
