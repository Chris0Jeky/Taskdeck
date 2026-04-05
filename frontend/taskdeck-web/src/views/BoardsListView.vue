<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useBoardStore } from '../store/boardStore'

const router = useRouter()
const boardStore = useBoardStore()

const newBoardName = ref('')
const showCreateForm = ref(false)

onMounted(async () => {
  // Catch the rethrown error — boardStore.error is already set by handleApiError
  // so the template can display it. Without this catch, Vue treats the unhandled
  // rejection as a lifecycle-hook error and may tear down the component.
  await boardStore.fetchBoards().catch(() => {})
})

async function createBoard() {
  if (!newBoardName.value.trim()) return

  try {
    const board = await boardStore.createBoard({
      name: newBoardName.value,
    })

    newBoardName.value = ''
    showCreateForm.value = false

    // Navigate to the new board
    router.push(`/boards/${board.id}`)
  } catch (error) {
    console.error('Failed to create board:', error)
  }
}

function goToBoard(id: string) {
  router.push(`/boards/${id}`)
}
</script>

<template>
  <div class="min-h-screen bg-surface">
    <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div class="flex justify-between items-center mb-8">
        <h1 class="td-page-title">My Boards</h1>
        <button
          @click="showCreateForm = !showCreateForm"
          class="td-btn td-btn--primary rounded-lg"
        >
          + New Board
        </button>
      </div>

      <!-- Create Board Form -->
      <div v-if="showCreateForm" class="mb-6 td-panel">
        <h2 class="text-lg font-semibold mb-4 text-on-surface">Create New Board</h2>
        <form @submit.prevent="createBoard" class="flex gap-3">
          <label for="new-board-name" class="sr-only">Board name</label>
          <input
            id="new-board-name"
            v-model="newBoardName"
            type="text"
            placeholder="Board name"
            class="flex-1 px-4 py-2 border border-outline-variant/15 rounded-lg bg-surface-container text-on-surface placeholder:text-on-surface/40 focus:outline-none focus:ring-1 focus:ring-primary-container"
          />
          <button
            type="submit"
            class="td-btn td-btn--primary rounded-lg"
          >
            Create
          </button>
          <button
            type="button"
            @click="showCreateForm = false"
            class="td-btn td-btn--secondary rounded-lg"
          >
            Cancel
          </button>
        </form>
      </div>

      <!-- Loading State -->
      <div v-if="boardStore.loading" class="text-center py-12">
        <div class="inline-block animate-spin rounded-full h-12 w-12 border-b-2 border-primary-container"></div>
        <p class="mt-4 text-on-surface/60">Loading boards...</p>
      </div>

      <!-- Error State -->
      <div v-else-if="boardStore.error" class="bg-ember/10 border border-ember rounded-lg p-4 text-ember" role="alert">
        {{ boardStore.error }}
      </div>

      <!-- Empty State -->
      <div v-else-if="boardStore.boards.length === 0" class="text-center py-12">
        <svg
          class="mx-auto h-12 w-12 text-on-surface/40"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
          />
        </svg>
        <h3 class="mt-2 text-sm font-medium text-on-surface">No boards</h3>
        <p class="mt-1 text-sm text-on-surface/60">Get started by creating a new board.</p>
        <div class="mt-6">
          <button
            @click="showCreateForm = true"
            class="td-btn td-btn--primary rounded-lg"
          >
            + Create Board
          </button>
        </div>
      </div>

      <!-- Boards Grid -->
      <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        <div
          v-for="board in boardStore.boards"
          :key="board.id"
          role="button"
          tabindex="0"
          :aria-label="`Open board: ${board.name}`"
          class="bg-surface-container-low rounded-lg shadow-[0_2px_8px_rgba(0,0,0,0.3)] hover:shadow-[0_4px_12px_rgba(0,0,0,0.3)] transition-shadow cursor-pointer p-6 border border-outline-variant/15 hover:bg-surface-container group"
          @click="goToBoard(board.id)"
          @keydown.enter="goToBoard(board.id)"
          @keydown.space.prevent="goToBoard(board.id)"
        >
          <h3 class="text-xl font-semibold text-on-surface mb-2">
            {{ board.name }}
          </h3>
          <p v-if="board.description" class="text-on-surface/60 text-sm line-clamp-2">
            {{ board.description }}
          </p>
          <div v-else class="text-on-surface/40 text-sm italic">No description</div>
          <div class="mt-4 text-xs text-on-surface/60">
            Created {{ new Date(board.createdAt).toLocaleDateString() }}
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
