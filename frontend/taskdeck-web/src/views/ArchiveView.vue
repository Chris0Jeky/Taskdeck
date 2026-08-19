<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { archiveApi } from '../api/archiveApi'
import { boardsApi } from '../api/boardsApi'
import { useToastStore } from '../store/toastStore'
import type { ArchiveItem } from '../types/archive'
import type { Board } from '../types/board'
import { normalizeRestoreStatus } from '../utils/archive'
import { getErrorDisplay } from '../composables/useErrorMapper'
import { HIDDEN_ARCHIVED_BOARDS_STORAGE_KEY } from '../utils/storageKeys'
import { logWarn } from '../utils/errorReporting'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'

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
  try {
    localStorage.setItem(
      HIDDEN_ARCHIVED_BOARDS_STORAGE_KEY,
      JSON.stringify(Array.from(hiddenArchivedBoardIds.value.values()))
    )
  } catch (error) {
    // Hidden-board preference persistence is best-effort; archive/restore flow should still complete.
    logWarn('Failed to persist hidden archived board preferences', error)
  }
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
  <div class="paper-archive">
    <header class="paper-archive__hero">
      <span class="tk-eyebrow paper-archive__eyebrow">Recovery</span>
      <h1 class="tk-h2 paper-archive__title">Archive</h1>
    </header>

    <div class="paper-archive__panel">
      <div class="paper-archive__section-header">
        <h2 class="tk-h3 paper-archive__section-title">Archived Boards</h2>
        <PaperHLBtn
          class="paper-archive__toggle-hidden"
          :disabled="hiddenArchivedBoardCount === 0 && !showHiddenBoards"
          @click="showHiddenBoards = !showHiddenBoards"
        >
          {{ showHiddenBoards ? 'Hide Hidden Boards' : `Show Hidden Boards (${hiddenArchivedBoardCount})` }}
        </PaperHLBtn>
      </div>

      <p class="paper-archive__helper">
        Archive removes a board from default board lists. Use <strong>Hide</strong> to keep old archived boards out of
        the default archive view without restoring them.
      </p>

      <div v-if="loadingBoards" class="paper-archive__state">Loading archived boards...</div>

      <div v-else-if="visibleArchivedBoards.length === 0" class="paper-archive__state">
        <span v-if="hiddenArchivedBoardCount > 0 && !showHiddenBoards">
          No archived boards in the default view. Use <strong>Show Hidden Boards</strong> to review hidden boards.
        </span>
        <span v-else>
          No archived boards found.
        </span>
      </div>

      <div v-else class="paper-archive__list paper-archive__list--section">
        <div v-for="board in visibleArchivedBoards" :key="board.id" class="paper-archive__row">
          <div class="paper-archive__info">
            <span class="paper-archive__badge">board</span>
            <span class="paper-archive__name">{{ board.name }}</span>
            <span v-if="isArchivedBoardHidden(board.id)" class="paper-archive__badge paper-archive__badge--muted">hidden</span>
            <span class="paper-archive__meta">
              archived board | updated {{ new Date(board.updatedAt).toLocaleString() }}
            </span>
          </div>
          <div class="paper-archive__actions">
            <PaperHLBtn
              variant="ember"
              class="paper-archive__restore-board"
              :disabled="boardRestoreBusyId === board.id"
              @click="handleRestoreBoard(board)"
            >
              {{ boardRestoreBusyId === board.id ? 'Restoring...' : 'Restore Board' }}
            </PaperHLBtn>
            <PaperHLBtn
              class="paper-archive__toggle-board"
              :disabled="boardRestoreBusyId === board.id"
              @click="handleToggleHiddenArchivedBoard(board)"
            >
              {{ isArchivedBoardHidden(board.id) ? 'Unhide' : 'Hide' }}
            </PaperHLBtn>
          </div>
        </div>
      </div>

      <h2 class="tk-h3 paper-archive__section-title">Archived Items</h2>
      <div class="paper-archive__toolbar">
        <select
          v-model="entityTypeFilter"
          class="paper-archive__input"
          aria-label="Filter by entity type"
          @change="loadArchiveItems"
        >
          <option value="all">All types</option>
          <option value="board">Boards</option>
          <option value="column">Columns</option>
          <option value="card">Cards</option>
        </select>
        <PaperHLBtn class="paper-archive__refresh" :disabled="loadingItems" @click="loadArchiveItems">
          Refresh Items
        </PaperHLBtn>
      </div>

      <div v-if="loadingItems" class="paper-archive__state">Loading archive...</div>

      <div v-else-if="filteredItems.length === 0" class="paper-archive__state">
        No archived items found in recovery inventory.
      </div>

      <div v-else class="paper-archive__list">
        <div v-for="item in filteredItems" :key="item.id" class="paper-archive__row">
          <div class="paper-archive__info">
            <span class="paper-archive__badge">{{ item.entityType }}</span>
            <span class="paper-archive__name">{{ item.name }}</span>
            <span class="paper-archive__meta">
              board {{ item.boardId }} | status {{ normalizeRestoreStatus(item.restoreStatus) }} | archived
              {{ new Date(item.archivedAt).toLocaleString() }}
            </span>
          </div>
          <PaperHLBtn
            variant="ember"
            class="paper-archive__restore-item"
            :disabled="restoreBusyId === item.id || normalizeRestoreStatus(item.restoreStatus) !== 'Available'"
            @click="handleRestore(item)"
          >
            {{ restoreBusyId === item.id ? 'Restoring...' : 'Restore' }}
          </PaperHLBtn>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — ArchiveView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell. */

.paper-archive {
  max-width: 960px;
  font-family: var(--sans, system-ui, sans-serif);
  /* Legacy ("off") mode: Paper vars are scoped to .paper/.paper-night, so a root
     that sets --ink must paint --paper alongside it or the near-black fallback
     lands on AppShell's Obsidian surface. No-op inside the Paper shell. */
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

.paper-archive__hero {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  margin-bottom: var(--s-6, 24px);
}

.paper-archive__eyebrow { color: var(--ember, #a8421f); }
.paper-archive__title { margin: 0; font-size: var(--t-h2, 32px); }

.paper-archive__panel {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-6, 24px);
}

.paper-archive__section-title {
  margin: var(--s-5, 20px) 0 var(--s-3, 12px);
  font-size: var(--t-lg, 18px);
}

.paper-archive__section-title:first-of-type { margin-top: 0; }

.paper-archive__section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
}

.paper-archive__helper {
  margin: calc(-1 * var(--s-1, 4px)) 0 var(--s-3, 12px);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
}

.paper-archive__toolbar {
  display: flex;
  gap: var(--s-2, 8px);
  margin-bottom: var(--s-4, 16px);
}

.paper-archive__state {
  text-align: center;
  padding: var(--s-6, 24px);
  color: var(--mute, #6c6557);
}

.paper-archive__list {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-archive__list--section { margin-bottom: var(--s-5, 20px); }

.paper-archive__row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
  padding: var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
}

.paper-archive__info {
  display: flex;
  align-items: center;
  gap: var(--s-3, 12px);
  min-width: 0;
}

.paper-archive__actions {
  display: flex;
  align-items: center;
  gap: var(--s-2, 8px);
}

.paper-archive__name {
  font-weight: 600;
  font-size: var(--t-md, 13.5px);
  color: var(--ink-deep, #0a0908);
}

.paper-archive__meta {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.paper-archive__badge {
  font-size: var(--t-xs, 10.5px);
  padding: 1px 6px;
  border-radius: var(--r-1, 2px);
  font-weight: 600;
  background: var(--paper-2, #ebe5d8);
  color: var(--ink-2, #3a352d);
  text-transform: uppercase;
}

.paper-archive__badge--muted {
  background: var(--paper-edge, #e3dac8);
  color: var(--mute, #6c6557);
}

.paper-archive__input {
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
}

.paper-archive__input:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}
</style>
