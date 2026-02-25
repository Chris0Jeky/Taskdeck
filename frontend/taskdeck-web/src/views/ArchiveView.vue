<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { archiveApi } from '../api/archiveApi'
import { boardsApi } from '../api/boardsApi'
import { useToastStore } from '../store/toastStore'
import type { ArchiveItem } from '../types/archive'
import type { Board } from '../types/board'
import { normalizeRestoreStatus } from '../utils/archive'
import { getErrorDisplay } from '../composables/useErrorMapper'

const HIDDEN_ARCHIVED_BOARDS_STORAGE_KEY = 'taskdeck_archive_hidden_boards'

const toast = useToastStore()
const loadingItems = ref(false)
const loadingBoards = ref(false)
const restoreBusyId = ref<string | null>(null)
const boardRestoreBusyId = ref<string | null>(null)
const archiveItems = ref<ArchiveItem[]>([])
const archivedBoards = ref<Board[]>([])
const hiddenArchivedBoardIds = ref<Set<string>>(new Set())
const showHiddenBoards = ref(false)
const entityTypeFilter = ref<'all' | 'board' | 'column' | 'card'>('all')

const filteredItems = computed(() => {
  if (entityTypeFilter.value === 'all') {
    return archiveItems.value
  }

  return archiveItems.value.filter(item => item.entityType.toLowerCase() === entityTypeFilter.value)
})

const hiddenArchivedBoardCount = computed(() =>
  archivedBoards.value.filter((board) => hiddenArchivedBoardIds.value.has(board.id)).length
)

const visibleArchivedBoards = computed(() => (
  archivedBoards.value.filter((board) => (
    showHiddenBoards.value || !hiddenArchivedBoardIds.value.has(board.id)
  ))
))

function loadHiddenArchivedBoardIds() {
  try {
    const raw = localStorage.getItem(HIDDEN_ARCHIVED_BOARDS_STORAGE_KEY)
    if (!raw) {
      hiddenArchivedBoardIds.value = new Set()
      return
    }

    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) {
      hiddenArchivedBoardIds.value = new Set()
      return
    }

    hiddenArchivedBoardIds.value = new Set(parsed.filter((item): item is string => typeof item === 'string'))
  } catch {
    hiddenArchivedBoardIds.value = new Set()
  }
}

function persistHiddenArchivedBoardIds() {
  localStorage.setItem(
    HIDDEN_ARCHIVED_BOARDS_STORAGE_KEY,
    JSON.stringify(Array.from(hiddenArchivedBoardIds.value.values()))
  )
}

function setArchivedBoardHidden(boardId: string, hidden: boolean) {
  const next = new Set(hiddenArchivedBoardIds.value)
  if (hidden) {
    next.add(boardId)
  } else {
    next.delete(boardId)
  }

  hiddenArchivedBoardIds.value = next
  persistHiddenArchivedBoardIds()
}

function isArchivedBoardHidden(boardId: string) {
  return hiddenArchivedBoardIds.value.has(boardId)
}

function reconcileHiddenArchivedBoards() {
  const archivedBoardIdSet = new Set(archivedBoards.value.map((board) => board.id))
  const next = new Set(
    Array.from(hiddenArchivedBoardIds.value.values()).filter((boardId) => archivedBoardIdSet.has(boardId))
  )

  if (next.size !== hiddenArchivedBoardIds.value.size) {
    hiddenArchivedBoardIds.value = next
    persistHiddenArchivedBoardIds()
  }
}

async function loadArchiveItems() {
  try {
    loadingItems.value = true
    const items = await archiveApi.getItems({
      entityType: entityTypeFilter.value === 'all' ? undefined : entityTypeFilter.value,
      limit: 200,
    })
    archiveItems.value = items
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load archive items').message)
  } finally {
    loadingItems.value = false
  }
}

async function loadArchivedBoards() {
  try {
    loadingBoards.value = true
    const boards = await boardsApi.getBoards(undefined, true)
    archivedBoards.value = boards.filter(board => board.isArchived)
    reconcileHiddenArchivedBoards()
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load archived boards').message)
  } finally {
    loadingBoards.value = false
  }
}

async function handleRestoreBoard(board: Board) {
  if (!confirm(`Restore board "${board.name}"?`)) {
    return
  }

  try {
    boardRestoreBusyId.value = board.id
    await boardsApi.updateBoard(board.id, { isArchived: false })
    setArchivedBoardHidden(board.id, false)
    archivedBoards.value = archivedBoards.value.filter(existing => existing.id !== board.id)
    toast.success(`Restored board "${board.name}"`)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to restore board').message)
  } finally {
    boardRestoreBusyId.value = null
  }
}

async function handleRestore(item: ArchiveItem) {
  if (!confirm(`Restore ${item.entityType} "${item.name}"?`)) {
    return
  }

  try {
    restoreBusyId.value = item.id
    const result = await archiveApi.restoreItem(item.entityType, item.entityId, {
      targetBoardId: null,
      restoreMode: 0,
      conflictStrategy: 0,
    })

    if (!result.success) {
      toast.error(result.errorMessage ?? 'Restore failed')
      return
    }

    archiveItems.value = archiveItems.value.filter(existing => existing.id !== item.id)
    toast.success(`Restored "${result.resolvedName ?? item.name}"`)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to restore archive item').message)
  } finally {
    restoreBusyId.value = null
  }
}

function handleToggleHiddenArchivedBoard(board: Board) {
  const shouldHide = !isArchivedBoardHidden(board.id)
  setArchivedBoardHidden(board.id, shouldHide)
  toast.success(shouldHide
    ? `Hidden board "${board.name}" from the default archive view`
    : `Unhid board "${board.name}" in the archive view`)
}

onMounted(() => {
  loadHiddenArchivedBoardIds()
  void loadArchiveItems()
  void loadArchivedBoards()
})
</script>

<template>
  <div class="td-archive">
    <h1 class="td-page-title">Archive</h1>

    <div class="td-panel">
      <div class="td-section-header">
        <h2 class="td-section-title">Archived Boards</h2>
        <button
          class="td-btn td-btn--secondary td-btn--sm"
          @click="showHiddenBoards = !showHiddenBoards"
          :disabled="hiddenArchivedBoardCount === 0 && !showHiddenBoards"
        >
          {{ showHiddenBoards ? 'Hide Hidden Boards' : `Show Hidden Boards (${hiddenArchivedBoardCount})` }}
        </button>
      </div>

      <p class="td-helper">
        Archive removes a board from default board lists. Use <strong>Hide</strong> to keep old archived boards out of
        the default archive view without restoring them.
      </p>

      <div v-if="loadingBoards" class="td-loading">Loading archived boards...</div>

      <div v-else-if="visibleArchivedBoards.length === 0" class="td-empty">
        <span v-if="hiddenArchivedBoardCount > 0 && !showHiddenBoards">
          No archived boards in the default view. Use <strong>Show Hidden Boards</strong> to review hidden boards.
        </span>
        <span v-else>
          No archived boards found.
        </span>
      </div>

      <div v-else class="td-archive-list td-archive-list--section">
        <div v-for="board in visibleArchivedBoards" :key="board.id" class="td-archive-row">
          <div class="td-archive-info">
            <span class="td-badge">board</span>
            <span class="td-archive-name">{{ board.name }}</span>
            <span v-if="isArchivedBoardHidden(board.id)" class="td-badge td-badge--muted">hidden</span>
            <span class="td-archive-meta">
              archived board | updated {{ new Date(board.updatedAt).toLocaleString() }}
            </span>
          </div>
          <div class="td-actions">
            <button
              class="td-btn td-btn--primary td-btn--sm"
              @click="handleRestoreBoard(board)"
              :disabled="boardRestoreBusyId === board.id"
            >
              {{ boardRestoreBusyId === board.id ? 'Restoring...' : 'Restore Board' }}
            </button>
            <button
              class="td-btn td-btn--secondary td-btn--sm"
              @click="handleToggleHiddenArchivedBoard(board)"
              :disabled="boardRestoreBusyId === board.id"
            >
              {{ isArchivedBoardHidden(board.id) ? 'Unhide' : 'Hide' }}
            </button>
          </div>
        </div>
      </div>

      <h2 class="td-section-title">Archived Items</h2>
      <div class="td-toolbar">
        <select v-model="entityTypeFilter" class="td-input" @change="loadArchiveItems">
          <option value="all">All types</option>
          <option value="board">Boards</option>
          <option value="column">Columns</option>
          <option value="card">Cards</option>
        </select>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="loadArchiveItems" :disabled="loadingItems">
          Refresh Items
        </button>
      </div>

      <div v-if="loadingItems" class="td-loading">Loading archive...</div>

      <div v-else-if="filteredItems.length === 0" class="td-empty">
        No archived items found in recovery inventory.
      </div>

      <div v-else class="td-archive-list">
        <div v-for="item in filteredItems" :key="item.id" class="td-archive-row">
          <div class="td-archive-info">
            <span class="td-badge">{{ item.entityType }}</span>
            <span class="td-archive-name">{{ item.name }}</span>
            <span class="td-archive-meta">
              board {{ item.boardId }} | status {{ normalizeRestoreStatus(item.restoreStatus) }} | archived
              {{ new Date(item.archivedAt).toLocaleString() }}
            </span>
          </div>
          <button
            class="td-btn td-btn--primary td-btn--sm"
            @click="handleRestore(item)"
            :disabled="restoreBusyId === item.id || normalizeRestoreStatus(item.restoreStatus) !== 'Available'"
          >
            {{ restoreBusyId === item.id ? 'Restoring...' : 'Restore' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-archive { max-width: 960px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-6); }
.td-section-title { font-size: var(--td-font-lg); font-weight: 600; color: var(--td-text-primary); margin-bottom: var(--td-space-3); margin-top: var(--td-space-5); }
.td-section-title:first-of-type { margin-top: 0; }
.td-section-header { display: flex; justify-content: space-between; align-items: center; gap: var(--td-space-3); }
.td-helper { margin-top: calc(-1 * var(--td-space-1)); margin-bottom: var(--td-space-3); font-size: var(--td-font-xs); color: var(--td-text-secondary); }
.td-toolbar { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-4); }
.td-loading, .td-empty { text-align: center; padding: var(--td-space-6); color: var(--td-text-secondary); }
.td-archive-list { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-archive-list--section { margin-bottom: var(--td-space-5); }
.td-archive-row { display: flex; justify-content: space-between; align-items: center; gap: var(--td-space-3); padding: var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); }
.td-archive-info { display: flex; align-items: center; gap: var(--td-space-3); min-width: 0; }
.td-actions { display: flex; align-items: center; gap: var(--td-space-2); }
.td-archive-name { font-weight: 500; font-size: var(--td-font-sm); }
.td-archive-meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.td-badge { font-size: var(--td-font-xs); padding: 1px 6px; border-radius: var(--td-radius-sm); font-weight: 600; background: var(--td-surface-tertiary); color: var(--td-text-secondary); text-transform: uppercase; }
.td-badge--muted { background: var(--td-surface-secondary); color: var(--td-text-tertiary); }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
</style>
