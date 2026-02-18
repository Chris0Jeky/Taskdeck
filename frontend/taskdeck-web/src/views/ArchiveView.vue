<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { archiveApi } from '../api/archiveApi'
import { boardsApi } from '../api/boardsApi'
import { useToastStore } from '../store/toastStore'
import type { ArchiveItem } from '../types/archive'
import type { Board } from '../types/board'
import { normalizeRestoreStatus } from '../utils/archive'
import { getErrorDisplay } from '../composables/useErrorMapper'

const toast = useToastStore()
const loadingItems = ref(false)
const loadingBoards = ref(false)
const restoreBusyId = ref<string | null>(null)
const boardRestoreBusyId = ref<string | null>(null)
const archiveItems = ref<ArchiveItem[]>([])
const archivedBoards = ref<Board[]>([])
const entityTypeFilter = ref<'all' | 'board' | 'column' | 'card'>('all')

const filteredItems = computed(() => {
  if (entityTypeFilter.value === 'all') {
    return archiveItems.value
  }

  return archiveItems.value.filter(item => item.entityType.toLowerCase() === entityTypeFilter.value)
})

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

onMounted(() => {
  void loadArchiveItems()
  void loadArchivedBoards()
})
</script>

<template>
  <div class="td-archive">
    <h1 class="td-page-title">Archive</h1>

    <div class="td-panel">
      <h2 class="td-section-title">Archived Boards</h2>

      <div v-if="loadingBoards" class="td-loading">Loading archived boards...</div>

      <div v-else-if="archivedBoards.length === 0" class="td-empty">
        No archived boards found.
      </div>

      <div v-else class="td-archive-list td-archive-list--section">
        <div v-for="board in archivedBoards" :key="board.id" class="td-archive-row">
          <div class="td-archive-info">
            <span class="td-badge">board</span>
            <span class="td-archive-name">{{ board.name }}</span>
            <span class="td-archive-meta">
              archived board | updated {{ new Date(board.updatedAt).toLocaleString() }}
            </span>
          </div>
          <button
            class="td-btn td-btn--primary td-btn--sm"
            @click="handleRestoreBoard(board)"
            :disabled="boardRestoreBusyId === board.id"
          >
            {{ boardRestoreBusyId === board.id ? 'Restoring...' : 'Restore Board' }}
          </button>
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
.td-toolbar { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-4); }
.td-loading, .td-empty { text-align: center; padding: var(--td-space-6); color: var(--td-text-secondary); }
.td-archive-list { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-archive-list--section { margin-bottom: var(--td-space-5); }
.td-archive-row { display: flex; justify-content: space-between; align-items: center; gap: var(--td-space-3); padding: var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); }
.td-archive-info { display: flex; align-items: center; gap: var(--td-space-3); min-width: 0; }
.td-archive-name { font-weight: 500; font-size: var(--td-font-sm); }
.td-archive-meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.td-badge { font-size: var(--td-font-xs); padding: 1px 6px; border-radius: var(--td-radius-sm); font-weight: 600; background: var(--td-surface-tertiary); color: var(--td-text-secondary); text-transform: uppercase; }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
</style>
