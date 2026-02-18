<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuditStore } from '../store/auditStore'
import { useBoardStore } from '../store/boardStore'
import { useSessionStore } from '../store/sessionStore'
import { useToastStore } from '../store/toastStore'

type ViewMode = 'board' | 'entity' | 'user'
type DiscoverableEntityType = 'Board' | 'Column' | 'Card' | 'Label'

interface SelectorOption {
  id: string
  label: string
  secondary?: string
}

const route = useRoute()
const router = useRouter()
const audit = useAuditStore()
const boards = useBoardStore()
const session = useSessionStore()
const toast = useToastStore()

const viewMode = ref<ViewMode>('board')
const selectedBoardId = ref('')
const selectedEntityType = ref<DiscoverableEntityType | ''>('')
const selectedEntityBoardId = ref('')
const selectedEntityId = ref('')
const limit = ref(50)

const loadingEntitySource = ref(false)
const suppressRouteSync = ref(false)
const loadedEntityBoardId = ref<string | null>(null)
const preserveRouteEntitySelection = ref(false)

const boardOptions = computed<SelectorOption[]>(() => {
  return [...boards.boards]
    .sort((left, right) => left.name.localeCompare(right.name))
    .map((board) => ({
      id: board.id,
      label: board.isArchived ? `${board.name} (Archived)` : board.name,
    }))
})

const requiresEntityBoardContext = computed(() => {
  return selectedEntityType.value !== '' && selectedEntityType.value !== 'Board'
})

const entityOptions = computed<SelectorOption[]>(() => {
  if (!selectedEntityType.value) {
    return []
  }

  if (selectedEntityType.value === 'Board') {
    return boardOptions.value.map((board) => ({
      id: board.id,
      label: board.label,
    }))
  }

  if (!requiresEntityBoardContext.value || boards.currentBoard?.id !== selectedEntityBoardId.value) {
    return []
  }

  if (selectedEntityType.value === 'Column') {
    return [...boards.currentBoard.columns]
      .sort((left, right) => left.position - right.position)
      .map((column) => ({
        id: column.id,
        label: column.name,
      }))
  }

  if (selectedEntityType.value === 'Card') {
    const columnNames = new Map(boards.currentBoard.columns.map((column) => [column.id, column.name]))
    return [...boards.currentBoardCards]
      .sort((left, right) => left.position - right.position)
      .map((card) => ({
        id: card.id,
        label: card.title,
        secondary: columnNames.get(card.columnId),
      }))
  }

  return [...boards.currentBoardLabels]
    .sort((left, right) => left.name.localeCompare(right.name))
    .map((label) => ({
      id: label.id,
      label: label.name,
    }))
})

const canFetch = computed(() => {
  if (viewMode.value === 'board') {
    return selectedBoardId.value.length > 0
  }

  if (viewMode.value === 'entity') {
    if (!selectedEntityType.value || !selectedEntityId.value) {
      return false
    }

    if (requiresEntityBoardContext.value) {
      return selectedEntityBoardId.value.length > 0
    }

    return true
  }

  return true
})

const selectedIdForCopy = computed(() => {
  if (viewMode.value === 'board') {
    return selectedBoardId.value
  }

  if (viewMode.value === 'entity') {
    return selectedEntityId.value
  }

  return session.userId ?? ''
})

const selectedIdLabel = computed(() => {
  if (viewMode.value === 'board') {
    return 'Board ID'
  }

  if (viewMode.value === 'entity') {
    return `${selectedEntityType.value || 'Entity'} ID`
  }

  return 'User ID'
})

function formatTimestamp(ts: string): string {
  return new Date(ts).toLocaleString()
}

function formatAction(action: string | number): string {
  if (typeof action === 'string') return action

  const map: Record<number, string> = {
    0: 'Created',
    1: 'Updated',
    2: 'Deleted',
    3: 'Archived',
    4: 'Unarchived',
    5: 'Moved',
    6: 'PermissionGranted',
    7: 'PermissionRevoked',
    8: 'OwnershipTransferred',
  }

  return map[action] ?? String(action)
}

function normalizeEntityType(rawEntityType: string): DiscoverableEntityType | '' {
  const normalized = rawEntityType.trim().toLowerCase()

  if (normalized === 'board') return 'Board'
  if (normalized === 'column') return 'Column'
  if (normalized === 'card') return 'Card'
  if (normalized === 'label') return 'Label'

  return ''
}

function applySelectorDefaults() {
  if (viewMode.value === 'board') {
    if (!selectedBoardId.value && boardOptions.value.length > 0) {
      selectedBoardId.value = boardOptions.value[0]!.id
    }
    return
  }

  if (viewMode.value === 'entity') {
    if (!selectedEntityType.value) {
      selectedEntityType.value = 'Board'
    }

    if (selectedEntityType.value === 'Board') {
      if (!selectedEntityId.value && boardOptions.value.length > 0) {
        selectedEntityId.value = boardOptions.value[0]!.id
      }
      return
    }

    if (!selectedEntityBoardId.value && boardOptions.value.length > 0) {
      selectedEntityBoardId.value = boardOptions.value[0]!.id
    }
  }
}

async function ensureEntitySourceBoardLoaded() {
  if (!requiresEntityBoardContext.value || !selectedEntityBoardId.value) {
    return
  }

  if (loadedEntityBoardId.value === selectedEntityBoardId.value) {
    return
  }

  loadingEntitySource.value = true
  try {
    await boards.fetchBoard(selectedEntityBoardId.value)
    loadedEntityBoardId.value = selectedEntityBoardId.value
  } catch {
    // boardStore handles toast + error state.
  } finally {
    loadingEntitySource.value = false
  }
}

function syncFromRoute() {
  const routeBoardId = typeof route.params.boardId === 'string' ? route.params.boardId : ''
  const routeEntityType = typeof route.params.entityType === 'string' ? normalizeEntityType(route.params.entityType) : ''
  const routeEntityId = typeof route.params.entityId === 'string' ? route.params.entityId : ''

  if (routeBoardId) {
    preserveRouteEntitySelection.value = false
    viewMode.value = 'board'
    selectedBoardId.value = routeBoardId
    selectedEntityType.value = ''
    selectedEntityBoardId.value = ''
    selectedEntityId.value = ''
    return
  }

  if (routeEntityType && routeEntityId) {
    preserveRouteEntitySelection.value = true
    viewMode.value = 'entity'
    selectedEntityType.value = routeEntityType
    selectedEntityId.value = routeEntityId
    selectedBoardId.value = ''

    if (routeEntityType === 'Board') {
      selectedEntityBoardId.value = ''
    }
    return
  }

  if (route.name === 'workspace-activity-user') {
    preserveRouteEntitySelection.value = false
    viewMode.value = 'user'
    selectedBoardId.value = ''
    selectedEntityType.value = ''
    selectedEntityBoardId.value = ''
    selectedEntityId.value = ''
    return
  }

  preserveRouteEntitySelection.value = false
  viewMode.value = 'board'
  selectedBoardId.value = ''
  selectedEntityType.value = ''
  selectedEntityBoardId.value = ''
  selectedEntityId.value = ''
}

async function loadSelectorData() {
  try {
    await boards.fetchBoards(undefined, true)
  } catch {
    // boardStore handles toast + error state.
  }
}

async function fetchHistory() {
  if (viewMode.value === 'board' && selectedBoardId.value) {
    await audit.fetchBoardHistory(selectedBoardId.value, limit.value)
    return
  }

  if (viewMode.value === 'entity' && selectedEntityType.value && selectedEntityId.value) {
    await audit.fetchEntityHistory(selectedEntityType.value, selectedEntityId.value, limit.value)
    return
  }

  if (viewMode.value === 'user') {
    await audit.fetchUserHistory(limit.value)
  }
}

async function fetchHistorySafe() {
  try {
    await fetchHistory()
  } catch {
    // Store handles toast + error state.
  }
}

function routeForCurrentSelection() {
  if (viewMode.value === 'board' && selectedBoardId.value) {
    return {
      name: 'workspace-activity-board',
      params: { boardId: selectedBoardId.value },
    }
  }

  if (viewMode.value === 'entity' && selectedEntityType.value && selectedEntityId.value) {
    return {
      name: 'workspace-activity-entity',
      params: {
        entityType: selectedEntityType.value,
        entityId: selectedEntityId.value,
      },
    }
  }

  if (viewMode.value === 'user') {
    return { name: 'workspace-activity-user' }
  }

  return { name: 'workspace-activity' }
}

async function handleFetchClick() {
  if (!canFetch.value) {
    if (viewMode.value === 'board') {
      toast.error('Select a board to fetch activity history.')
      return
    }

    if (viewMode.value === 'entity') {
      toast.error('Select an entity type and item to fetch activity history.')
      return
    }
  }

  suppressRouteSync.value = true
  try {
    await router.push(routeForCurrentSelection())
    await fetchHistorySafe()
  } finally {
    suppressRouteSync.value = false
  }
}

async function copySelectedId() {
  const id = selectedIdForCopy.value
  if (!id) {
    return
  }

  if (!navigator.clipboard?.writeText) {
    toast.error('Clipboard is not available in this browser.')
    return
  }

  try {
    await navigator.clipboard.writeText(id)
    toast.success('Copied ID to clipboard')
  } catch {
    toast.error('Failed to copy ID')
  }
}

watch(viewMode, async (mode) => {
  if (mode === 'board') {
    selectedEntityType.value = ''
    selectedEntityBoardId.value = ''
    selectedEntityId.value = ''
    applySelectorDefaults()
    return
  }

  if (mode === 'entity') {
    selectedBoardId.value = ''
    applySelectorDefaults()
    await ensureEntitySourceBoardLoaded()
    return
  }

  selectedBoardId.value = ''
  selectedEntityType.value = ''
  selectedEntityBoardId.value = ''
  selectedEntityId.value = ''
})

watch(selectedEntityType, async (nextType) => {
  selectedEntityId.value = ''

  if (!nextType) {
    selectedEntityBoardId.value = ''
    return
  }

  if (nextType === 'Board') {
    selectedEntityBoardId.value = ''
    applySelectorDefaults()
    return
  }

  applySelectorDefaults()
  loadedEntityBoardId.value = null
  await ensureEntitySourceBoardLoaded()
})

watch(selectedEntityBoardId, async () => {
  selectedEntityId.value = ''
  loadedEntityBoardId.value = null

  await ensureEntitySourceBoardLoaded()
})

watch(entityOptions, (options) => {
  if (viewMode.value !== 'entity') {
    return
  }

  if (options.length === 0) {
    if (preserveRouteEntitySelection.value && selectedEntityId.value) {
      return
    }

    selectedEntityId.value = ''
    return
  }

  const hasSelectedEntity = options.some((option) => option.id === selectedEntityId.value)
  if (!hasSelectedEntity) {
    if (preserveRouteEntitySelection.value && selectedEntityId.value) {
      return
    }

    selectedEntityId.value = options[0]!.id
  }
})

watch(boardOptions, () => {
  applySelectorDefaults()
})

onMounted(async () => {
  await loadSelectorData()
  syncFromRoute()
  applySelectorDefaults()
  await ensureEntitySourceBoardLoaded()
  await fetchHistorySafe()
  preserveRouteEntitySelection.value = false
})

watch(
  () => route.fullPath,
  async () => {
    if (suppressRouteSync.value) {
      return
    }

    syncFromRoute()
    applySelectorDefaults()
    await ensureEntitySourceBoardLoaded()
    await fetchHistorySafe()
    preserveRouteEntitySelection.value = false
  }
)
</script>

<template>
  <div class="td-activity">
    <h1 class="td-page-title">Activity</h1>

    <div class="td-activity__controls">
      <div class="td-form-row">
        <select id="activity-view-mode" v-model="viewMode" class="td-input" aria-label="Activity view mode">
          <option value="board">Board History</option>
          <option value="entity">Entity History</option>
          <option value="user">User History</option>
        </select>

        <select
          v-if="viewMode === 'board'"
          id="activity-board-select"
          v-model="selectedBoardId"
          class="td-input"
          aria-label="Select board"
        >
          <option value="" disabled>Select board...</option>
          <option v-for="board in boardOptions" :key="board.id" :value="board.id">
            {{ board.label }}
          </option>
        </select>

        <template v-if="viewMode === 'entity'">
          <select id="activity-entity-type" v-model="selectedEntityType" class="td-input" aria-label="Select entity type">
            <option value="" disabled>Select entity type...</option>
            <option value="Board">Board</option>
            <option value="Column">Column</option>
            <option value="Card">Card</option>
            <option value="Label">Label</option>
          </select>

          <select
            v-if="requiresEntityBoardContext"
            id="activity-entity-board-select"
            v-model="selectedEntityBoardId"
            class="td-input"
            aria-label="Select board context"
          >
            <option value="" disabled>Select board context...</option>
            <option v-for="board in boardOptions" :key="board.id" :value="board.id">
              {{ board.label }}
            </option>
          </select>

          <select
            v-if="selectedEntityType"
            id="activity-entity-select"
            v-model="selectedEntityId"
            class="td-input"
            :disabled="loadingEntitySource || entityOptions.length === 0"
            aria-label="Select entity"
          >
            <option value="" disabled>Select entity...</option>
            <option v-for="option in entityOptions" :key="option.id" :value="option.id">
              {{ option.secondary ? `${option.label} (${option.secondary})` : option.label }}
            </option>
          </select>
        </template>

        <div v-if="viewMode === 'user'" class="td-current-user">
          Current user: <strong>{{ session.username || 'me' }}</strong>
        </div>

        <select v-model.number="limit" class="td-input td-input--sm" aria-label="Activity limit">
          <option :value="25">25</option>
          <option :value="50">50</option>
          <option :value="100">100</option>
        </select>

        <button class="td-btn td-btn--primary td-btn--sm" :disabled="!canFetch" @click="handleFetchClick">
          Fetch
        </button>
      </div>

      <div v-if="loadingEntitySource" class="td-helper">Loading entities for selected board...</div>
      <div
        v-else-if="viewMode === 'entity' && selectedEntityType && entityOptions.length === 0"
        class="td-helper"
      >
        No entities found for current selection.
      </div>

      <div v-if="selectedIdForCopy" class="td-id-affordance">
        <span class="td-id-affordance__label">{{ selectedIdLabel }}</span>
        <code class="td-id-affordance__value">{{ selectedIdForCopy }}</code>
        <button
          class="td-btn td-btn--ghost td-btn--sm"
          :aria-label="`Copy ${selectedIdLabel} to clipboard`"
          @click="copySelectedId"
        >
          Copy ID
        </button>
      </div>
    </div>

    <div v-if="audit.loading" class="td-loading">Loading activity...</div>

    <div v-else class="td-timeline">
      <div v-if="audit.entries.length === 0" class="td-empty">No activity entries found.</div>
      <div v-for="entry in audit.entries" :key="entry.id" class="td-timeline__entry">
        <div class="td-timeline__dot"></div>
        <div class="td-timeline__content">
          <div class="td-timeline__header">
            <span class="td-timeline__action">{{ formatAction(entry.action) }}</span>
            <span class="td-timeline__time">{{ formatTimestamp(entry.timestamp) }}</span>
          </div>
          <div class="td-timeline__details">
            <span class="td-timeline__entity">{{ entry.entityType }} - {{ entry.entityId }}</span>
            <span v-if="entry.userName" class="td-timeline__actor">by {{ entry.userName }}</span>
          </div>
          <div v-if="entry.changes" class="td-timeline__message">{{ entry.changes }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-activity { max-width: 860px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-activity__controls { background: var(--td-surface-primary); border-radius: var(--td-radius-lg); padding: var(--td-space-4); margin-bottom: var(--td-space-4); border: 1px solid var(--td-border-default); }
.td-form-row { display: flex; gap: var(--td-space-2); flex-wrap: wrap; align-items: center; }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); min-width: 180px; }
.td-input--sm { min-width: 80px; max-width: 90px; }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-current-user { font-size: var(--td-font-sm); color: var(--td-text-secondary); padding: var(--td-space-1) var(--td-space-2); background: var(--td-surface-secondary); border-radius: var(--td-radius-sm); }
.td-helper { margin-top: var(--td-space-2); color: var(--td-text-secondary); font-size: var(--td-font-sm); }
.td-id-affordance { margin-top: var(--td-space-3); display: flex; align-items: center; gap: var(--td-space-2); flex-wrap: wrap; }
.td-id-affordance__label { font-size: var(--td-font-xs); color: var(--td-text-tertiary); text-transform: uppercase; letter-spacing: 0.04em; }
.td-id-affordance__value { font-size: var(--td-font-xs); background: var(--td-surface-secondary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-sm); padding: 0 var(--td-space-2); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover { background: var(--td-color-primary-hover); }
.td-btn--ghost { background: var(--td-surface-secondary); color: var(--td-text-secondary); border: 1px solid var(--td-border-default); }
.td-btn--ghost:hover { background: var(--td-surface-tertiary); }
.td-loading { text-align: center; padding: var(--td-space-8); color: var(--td-text-secondary); }
.td-empty { text-align: center; padding: var(--td-space-8); color: var(--td-text-tertiary); }
.td-timeline { display: flex; flex-direction: column; gap: 0; }
.td-timeline__entry { display: flex; gap: var(--td-space-4); padding: var(--td-space-4) 0; border-left: 2px solid var(--td-border-default); margin-left: var(--td-space-3); padding-left: var(--td-space-4); position: relative; }
.td-timeline__dot { position: absolute; left: -6px; top: var(--td-space-5); width: 10px; height: 10px; background: var(--td-color-primary); border-radius: 50%; border: 2px solid var(--td-surface-secondary); }
.td-timeline__content { flex: 1; background: var(--td-surface-primary); border-radius: var(--td-radius-md); padding: var(--td-space-3); border: 1px solid var(--td-border-default); }
.td-timeline__header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--td-space-1); }
.td-timeline__action { font-weight: 600; font-size: var(--td-font-sm); color: var(--td-text-primary); }
.td-timeline__time { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-timeline__details { font-size: var(--td-font-xs); color: var(--td-text-secondary); display: flex; gap: var(--td-space-2); }
.td-timeline__entity { font-family: monospace; }
.td-timeline__message { font-size: var(--td-font-sm); color: var(--td-text-secondary); margin-top: var(--td-space-2); padding-top: var(--td-space-2); border-top: 1px solid var(--td-border-default); }
</style>
