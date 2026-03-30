<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useBoardStore } from '../store/boardStore'
import { useSessionStore } from '../store/sessionStore'
import { useKeyboardShortcuts } from '../composables/useKeyboardShortcuts'
import { createBoardRealtimeController } from '../composables/useBoardRealtime'
import { useBoardDragDrop } from '../composables/useBoardDragDrop'
import { useBoardKeyboardNav } from '../composables/useBoardKeyboardNav'
import { usePerformanceMark } from '../composables/usePerformanceMark'
import BoardToolbar from '../components/board/BoardToolbar.vue'
import BoardActionRail from '../components/board/BoardActionRail.vue'
import BoardCanvas from '../components/board/BoardCanvas.vue'
import BoardDialogHost from '../components/board/BoardDialogHost.vue'
import FilterPanel from '../components/board/FilterPanel.vue'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import type { BoardPresenceMember } from '../types/realtime'
import type { CardFilters } from '../store/boardStore'
import { isClientOnboardingDemoBoardName } from '../utils/boardDemo'

const route = useRoute()
const router = useRouter()
const boardStore = useBoardStore()
const sessionStore = useSessionStore()

const newColumnName = ref('')
const showColumnForm = ref(false)
const showBoardSettings = ref(false)
const showLabelManager = ref(false)
const showStarterPackCatalog = ref(false)
const showKeyboardHelp = ref(false)
const showFilterPanel = ref(false)
const showBoardCaptureModal = ref(false)
const presenceMembers = ref<BoardPresenceMember[]>([])

const boardLoadPerf = usePerformanceMark('board-load')

/**
 * Build a presence entry for the current authenticated user so the panel
 * shows immediately on mount, without waiting for the async SignalR
 * BoardJoined push event. This eliminates the empty-state flicker.
 */
function currentUserPresenceSeed(): BoardPresenceMember[] {
  const uid = sessionStore.userId
  if (!uid) return []
  return [{ userId: uid, displayName: sessionStore.username ?? null, editingCardId: null }]
}


const boardId = ref(route.params.id as string)
const realtime = createBoardRealtimeController({
  fetchBoard: async (id: string) => {
    await boardStore.fetchBoard(id)
  },
  onPresenceChanged: (snapshot) => {
    if (snapshot.boardId !== boardId.value) {
      return
    }

    presenceMembers.value = snapshot.members
    boardStore.setBoardPresenceMembers(snapshot.members)
  },
})

// Sort columns by position
const sortedColumns = computed(() => {
  if (!boardStore.currentBoard) return []
  return [...boardStore.currentBoard.columns].sort((a, b) => a.position - b.position)
})
const isDemoBoard = computed(() => isClientOnboardingDemoBoardName(boardStore.currentBoard?.name))

// Composables
const {
  draggedColumn,
  dragOverColumnId,
  draggedCard,
  handleColumnDragStart,
  handleColumnDragEnd,
  handleColumnDragOver,
  handleColumnDragLeave,
  handleColumnDrop,
  handleCardDragStart,
  handleCardDragEnd,
} = useBoardDragDrop(() => boardId.value, sortedColumns)

const {
  selectedCardId,
  selectNextCard,
  selectPreviousCard,
  selectNextColumn,
  selectPreviousColumn,
  openSelectedCard,
  createCardInSelectedColumn,
  resetSelection,
} = useBoardKeyboardNav(sortedColumns)

function applyPresenceSeed() {
  const seed = currentUserPresenceSeed()
  presenceMembers.value = seed
  boardStore.setBoardPresenceMembers(seed)
}

onMounted(async () => {
  boardLoadPerf.start()
  try {
    // Seed presence with the current user immediately so the panel never
    // shows "No active collaborators" while waiting for the SignalR
    // BoardJoined push event (fixes #523 flicker).
    applyPresenceSeed()
    boardStore.setEditingCard(null)
    await boardStore.fetchBoard(boardId.value)
    await realtime.start(boardId.value)
  } catch (error) {
    console.error('Failed to load board:', error)
  } finally {
    boardLoadPerf.end()
  }
})

watch(
  () => route.params.id,
  async (nextId) => {
    const nextBoardId = typeof nextId === 'string' ? nextId : ''
    if (!nextBoardId || nextBoardId === boardId.value) {
      return
    }

    boardId.value = nextBoardId
    resetSelection()
    // Seed with current user on board switch for the same reason as onMounted.
    applyPresenceSeed()
    boardStore.setEditingCard(null)

    try {
      await boardStore.fetchBoard(boardId.value)
      await realtime.switchBoard(boardId.value)
    } catch (error) {
      console.error('Failed to switch board:', error)
    }
  }
)

watch(
  () => boardStore.editingCardId,
  async (nextEditingCardId) => {
    await realtime.setEditingCard(nextEditingCardId)
  }
)

onBeforeUnmount(() => {
  presenceMembers.value = []
  boardStore.setBoardPresenceMembers([])
  boardStore.setEditingCard(null)
  void realtime.stop()
})

async function createColumn() {
  if (!newColumnName.value.trim()) return

  try {
    await boardStore.createColumn(boardId.value, {
      name: newColumnName.value,
    })
    newColumnName.value = ''
    showColumnForm.value = false
  } catch (error) {
    console.error('Failed to create column:', error)
  }
}

function goBack() {
  router.push('/boards')
}

function toggleKeyboardHelp() {
  showKeyboardHelp.value = !showKeyboardHelp.value
}

function toggleFilterPanel() {
  showFilterPanel.value = !showFilterPanel.value
}

function openBoardCaptureModal() {
  showBoardCaptureModal.value = true
}

function openBoardReview() {
  void router.push({
    name: 'workspace-review',
    query: { boardId: boardId.value },
  })
}

function openBoardInbox() {
  void router.push({
    name: 'workspace-inbox',
    query: { boardId: boardId.value },
  })
}

function openBoardChat() {
  void router.push({
    name: 'workspace-automations-chat',
    query: { boardId: boardId.value },
  })
}

function openBoardCardComposer() {
  if (sortedColumns.value.length === 0) {
    showColumnForm.value = true
    return
  }

  createCardInSelectedColumn()
}

function handleFiltersUpdate(newFilters: CardFilters) {
  boardStore.updateFilters(newFilters)
}

function closeOpenUi() {
  if (showKeyboardHelp.value) {
    showKeyboardHelp.value = false
    return
  }

  if (showLabelManager.value) {
    showLabelManager.value = false
    return
  }

  if (showStarterPackCatalog.value) {
    showStarterPackCatalog.value = false
    return
  }

  if (showBoardSettings.value) {
    showBoardSettings.value = false
    return
  }

  if (showFilterPanel.value) {
    showFilterPanel.value = false
    return
  }

  if (showColumnForm.value) {
    showColumnForm.value = false
    return
  }

  const cancelAddCardButton = document.querySelector(
    '[data-action="cancel-add-card"]'
  ) as HTMLButtonElement | null
  if (cancelAddCardButton) {
    cancelAddCardButton.click()
    return
  }

  // Escape from a clean board canvas returns to the boards list.
  router.push('/workspace/boards')
}

// Setup keyboard shortcuts
useKeyboardShortcuts([
  // Navigation
  { key: 'j', description: 'Next card', action: selectNextCard },
  { key: 'ArrowDown', description: 'Next card', action: selectNextCard },
  { key: 'k', description: 'Previous card', action: selectPreviousCard },
  { key: 'ArrowUp', description: 'Previous card', action: selectPreviousCard },
  { key: 'h', description: 'Previous column', action: selectPreviousColumn },
  { key: 'ArrowLeft', description: 'Previous column', action: selectPreviousColumn },
  { key: 'l', description: 'Next column', action: selectNextColumn },
  { key: 'ArrowRight', description: 'Next column', action: selectNextColumn },

  // Actions
  { key: 'Enter', description: 'Open selected card', action: openSelectedCard },
  { key: 'n', description: 'New card in current column', action: createCardInSelectedColumn },
  { key: 'Escape', description: 'Close open dialog/panel', action: closeOpenUi },

  // Help
  { key: '?', description: 'Toggle keyboard shortcuts help', action: toggleKeyboardHelp },
  { key: 'f', description: 'Toggle filter panel', action: toggleFilterPanel },
])
</script>

<template>
  <div class="min-h-screen bg-surface">
    <!-- Header -->
    <div class="bg-surface-container border-b border-outline-variant/15">
      <div class="max-w-full px-4 sm:px-6 lg:px-8 py-4">
        <BoardToolbar
          v-if="boardStore.currentBoard"
          :board-name="boardStore.currentBoard.name"
          :board-description="boardStore.currentBoard.description"
          :is-demo-board="isDemoBoard"
          :presence-members="presenceMembers"
          :show-filter-panel="showFilterPanel"
          :filtered-card-count="boardStore.filteredCardCount"
          :total-card-count="boardStore.totalCardCount"
          @back="goBack"
          @toggle-filter="toggleFilterPanel"
          @show-keyboard-help="showKeyboardHelp = true"
          @show-label-manager="showLabelManager = true"
          @show-starter-pack-catalog="showStarterPackCatalog = true"
          @show-board-settings="showBoardSettings = true"
          @toggle-column-form="showColumnForm = !showColumnForm"
        />

        <BoardActionRail
          v-if="boardStore.currentBoard"
          @capture="openBoardCaptureModal"
          @chat="openBoardChat"
          @review="openBoardReview"
          @inbox="openBoardInbox"
          @add-card="openBoardCardComposer"
        />

        <WorkspaceHelpCallout
          v-if="boardStore.currentBoard"
          topic="board"
          class="mt-4"
          title="What should happen on a board?"
          description="Boards are where approved work appears. Capture new input, review the proposed changes, then come back here to manage the result."
        >
          <template #actions>
            <button class="td-btn td-btn--secondary td-btn--sm" @click="openBoardCaptureModal">Capture here</button>
            <button class="td-btn td-btn--secondary td-btn--sm" @click="openBoardReview">Review proposals</button>
          </template>
        </WorkspaceHelpCallout>

        <!-- Create Column Form -->
        <div v-if="showColumnForm" class="td-column-form">
          <form @submit.prevent="createColumn" class="td-column-form__row">
            <label for="td-new-column-name" class="sr-only">Column name</label>
            <input
              id="td-new-column-name"
              v-model="newColumnName"
              type="text"
              placeholder="Column name"
              class="td-column-form__input"
              autofocus
              @keydown.enter.prevent="createColumn"
            />
            <button
              type="submit"
              class="td-column-form__btn td-column-form__btn--primary"
            >
              Create
            </button>
            <button
              type="button"
              @click="showColumnForm = false"
              class="td-column-form__btn td-column-form__btn--secondary"
            >
              Cancel
            </button>
          </form>
        </div>
      </div>
    </div>

    <!-- Filter Panel -->
    <FilterPanel
      :is-open="showFilterPanel"
      :labels="boardStore.currentBoardLabels"
      :active-filters="boardStore.filters"
      @update:filters="handleFiltersUpdate"
      @toggle="toggleFilterPanel"
    />

    <!-- Loading State -->
    <div v-if="boardStore.loading && !boardStore.currentBoard" class="flex justify-center items-center py-12" aria-live="polite">
      <div class="inline-block animate-spin rounded-full h-12 w-12 border-b-2 border-primary-container" role="status" aria-label="Loading board">
        <span class="sr-only">Loading board...</span>
      </div>
    </div>

    <!-- Error State -->
    <div v-else-if="boardStore.error" class="max-w-7xl mx-auto px-4 py-8">
      <div class="bg-error-container/20 border border-error/20 rounded-lg p-4 text-error" role="alert">
        {{ boardStore.error }}
      </div>
    </div>

    <!-- Board Content -->
    <BoardCanvas
      v-else-if="boardStore.currentBoard"
      :sorted-columns="sortedColumns"
      :cards-by-column="boardStore.cardsByColumn"
      :labels="boardStore.currentBoardLabels"
      :board-id="boardId"
      :has-columns="boardStore.currentBoard.columns.length > 0"
      :dragged-column="draggedColumn"
      :drag-over-column-id="dragOverColumnId"
      :dragged-card="draggedCard"
      :selected-card-id="selectedCardId"
      @column-drag-start="handleColumnDragStart"
      @column-drag-end="handleColumnDragEnd"
      @column-drag-over="handleColumnDragOver"
      @column-drag-leave="handleColumnDragLeave"
      @column-drop="handleColumnDrop"
      @card-drag-start="handleCardDragStart"
      @card-drag-end="handleCardDragEnd"
    />

    <!-- Dialog Host -->
    <BoardDialogHost
      :board="boardStore.currentBoard"
      :board-id="boardId"
      :board-labels="boardStore.currentBoardLabels"
      :show-board-settings="showBoardSettings"
      :show-label-manager="showLabelManager"
      :show-starter-pack-catalog="showStarterPackCatalog"
      :show-keyboard-help="showKeyboardHelp"
      :show-capture-modal="showBoardCaptureModal"
      @update:show-board-settings="showBoardSettings = $event"
      @update:show-label-manager="showLabelManager = $event"
      @update:show-starter-pack-catalog="showStarterPackCatalog = $event"
      @update:show-keyboard-help="showKeyboardHelp = $event"
      @update:show-capture-modal="showBoardCaptureModal = $event"
    />
  </div>
</template>

<style scoped>
/* ── Create Column Form — token-based ── */
.td-column-form {
  margin-top: var(--td-space-5);
  background: var(--td-surface-container);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-5);
}

.td-column-form__row {
  display: flex;
  gap: var(--td-space-4);
}

.td-column-form__input {
  flex: 1;
  padding: var(--td-space-2) var(--td-space-5);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  background: var(--td-surface-container-lowest);
  color: var(--td-text-primary);
  font-size: var(--td-font-base);
}

.td-column-form__input:focus {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

.td-column-form__input::placeholder {
  color: var(--td-text-tertiary);
}

.td-column-form__btn {
  padding: var(--td-space-2) var(--td-space-6);
  font-size: var(--td-font-sm);
  font-weight: 500;
  border-radius: var(--td-radius-lg);
  transition:
    background-color var(--td-transition-fast),
    filter var(--td-transition-fast);
}

.td-column-form__btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-column-form__btn--primary:hover {
  background: var(--td-color-primary-hover);
}

.td-column-form__btn--primary:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

.td-column-form__btn--secondary {
  background: var(--td-surface-bright);
  color: var(--td-text-muted);
}

.td-column-form__btn--secondary:hover {
  background: var(--td-surface-container-highest);
}

.td-column-form__btn--secondary:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}
</style>
