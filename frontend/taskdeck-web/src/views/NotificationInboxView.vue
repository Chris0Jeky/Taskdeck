<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useNotificationStore } from '../store/notificationStore'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type { NotificationItem } from '../types/notifications'

const notifications = useNotificationStore()
const route = useRoute()
const router = useRouter()
const unreadOnly = ref(false)
const inlineError = ref<string | null>(null)

const items = computed(() => notifications.notifications)
const unreadCount = computed(() => items.value.filter((item) => !item.isRead).length)
const activeBoardId = computed(() => {
  const candidate = route.query.boardId
  if (Array.isArray(candidate)) {
    return typeof candidate[0] === 'string' ? candidate[0].trim() : ''
  }

  return typeof candidate === 'string' ? candidate.trim() : ''
})

function formatType(value: number | string): string {
  const normalized = String(value)
  if (normalized === '0' || normalized === 'Mention') return 'Mention'
  if (normalized === '1' || normalized === 'Assignment') return 'Assignment'
  if (normalized === '2' || normalized === 'ProposalOutcome') return 'Proposal Outcome'
  return normalized
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
  <div class="td-notifications">
    <header class="td-notifications__header">
      <div>
        <h1 class="td-page-title">Notifications</h1>
        <p class="td-notifications__subtitle">
          {{ unreadCount }} unread
        </p>
        <p v-if="activeBoardId" class="td-notifications__board-context">
          Showing notifications linked to board {{ activeBoardId }}.
        </p>
      </div>
      <button class="td-btn td-btn--secondary" @click="loadNotifications">
        Refresh
      </button>
    </header>

    <div class="td-notifications__controls">
      <label class="td-toggle">
        <input v-model="unreadOnly" type="checkbox" />
        <span>Show unread only</span>
      </label>
    </div>

    <div v-if="inlineError" class="td-alert td-alert--error" role="alert">
      {{ inlineError }}
    </div>

    <div v-if="notifications.loading" class="td-placeholder">Loading notifications...</div>
    <div v-else-if="items.length === 0" class="td-placeholder">No notifications found.</div>

    <ul v-else class="td-list">
      <li
        v-for="item in items"
        :key="item.id"
        :class="['td-notification-row', { 'td-notification-row--unread': !item.isRead }]"
      >
        <div class="td-notification-row__main">
          <div class="td-notification-row__title">{{ item.title }}</div>
          <div class="td-notification-row__message">{{ item.message }}</div>
          <div class="td-notification-row__meta">
            <span v-if="item.boardId">board {{ item.boardId }}</span>
            <span>{{ formatType(item.type) }}</span>
            <span>{{ formatCadence(item.cadence) }}</span>
            <span>{{ new Date(item.createdAt).toLocaleString() }}</span>
          </div>
        </div>
        <div class="td-notification-row__actions">
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
    </ul>
  </div>
</template>

<style scoped>
.td-notifications {
  max-width: 920px;
}

.td-notifications__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--td-space-4);
  margin-bottom: var(--td-space-4);
}

.td-notifications__subtitle {
  margin-top: var(--td-space-1);
  color: var(--td-text-secondary);
}

.td-notifications__board-context {
  margin-top: var(--td-space-2);
  color: var(--td-color-primary);
  font-size: var(--td-font-sm);
  font-weight: 600;
}

.td-notifications__controls {
  margin-bottom: var(--td-space-4);
}

.td-toggle {
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-2);
  color: var(--td-text-secondary);
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

.td-placeholder {
  color: var(--td-text-secondary);
  padding: var(--td-space-6) 0;
}

.td-list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  list-style: none;
  margin: 0;
  padding: 0;
}

.td-notification-row {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-4);
  align-items: flex-start;
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-4);
}

.td-notification-row--unread {
  border-color: var(--td-color-primary);
}

.td-notification-row__main {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-notification-row__title {
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-notification-row__message {
  color: var(--td-text-secondary);
}

.td-notification-row__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-3);
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
}

.td-notification-row__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: var(--td-space-2);
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
